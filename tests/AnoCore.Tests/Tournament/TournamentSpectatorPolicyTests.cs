using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Modules.Tournament;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentSpectatorPolicyTests
{
    private static readonly Guid MatchId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly PlayerId A =
        new(76561198000201001);
    private static readonly PlayerId B =
        new(76561198000201002);
    private static readonly PlayerId CoachA =
        new(76561198000201011);
    private static readonly PlayerId CoachB =
        new(76561198000201012);
    private static readonly PlayerId Viewer =
        new(76561198000201020);
    private static readonly PlayerId Outsider =
        new(76561198000201999);
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 19, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Policy_RosterTakesPrecedenceAndConfiguredSpectatorsAreDeterministic()
    {
        var policy = new TournamentSpectatorPolicy(
            Configuration(),
            allowPublicSpectators: false,
            teamACoaches: [CoachA],
            teamBCoaches: [CoachB],
            spectatorWhitelist: [Viewer]);

        Assert.AreEqual(
            TournamentSpectatorAccessKind.RosterPlayer,
            policy.Decide(A).Kind);
        Assert.AreEqual(
            TournamentSpectatorAccessKind.TeamACoach,
            policy.Decide(CoachA).Kind);
        Assert.AreEqual(
            TournamentSpectatorAccessKind.TeamBCoach,
            policy.Decide(CoachB).Kind);
        Assert.AreEqual(
            TournamentSpectatorAccessKind.WhitelistedSpectator,
            policy.Decide(Viewer).Kind);
        Assert.AreEqual(
            TournamentSpectatorAccessKind.Denied,
            policy.Decide(Outsider).Kind);
    }

    [TestMethod]
    public void Policy_RejectsRosterCoachAndCrossRoleOverlap()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new TournamentSpectatorPolicy(
                Configuration(), teamACoaches: [A]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new TournamentSpectatorPolicy(
                Configuration(),
                teamACoaches: [CoachA],
                teamBCoaches: [CoachA]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new TournamentSpectatorPolicy(
                Configuration(),
                teamACoaches: [CoachA],
                spectatorWhitelist: [CoachA]));
    }

    [TestMethod]
    public async Task CoachAndWhitelistedViewer_AreMovedToSpectatorButRosterIsUntouched()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        using var source = new TournamentSpectatorPolicySource();
        source.Replace(new TournamentSpectatorPolicy(
            Configuration(),
            teamACoaches: [CoachA],
            spectatorWhitelist: [Viewer]));
        var transport = new RecordingTransport();
        using var enforcement = new TournamentSpectatorEnforcement(
            events,
            players,
            new AssignmentSource(MatchId),
            source,
            transport);

        await players.ConnectAsync(new PlayerConnection(
            A, "A", PlayerTeam.Terrorist, true, Now));
        await players.ConnectAsync(new PlayerConnection(
            CoachA, "Coach", PlayerTeam.CounterTerrorist, true, Now));
        await players.ConnectAsync(new PlayerConnection(
            Viewer, "Viewer", PlayerTeam.Terrorist, true, Now));

        Assert.AreEqual(2, transport.Spectator.Count);
        CollectionAssert.AreEquivalent(
            new[] { CoachA, Viewer },
            transport.Spectator.Select(value => value.PlayerId).ToArray());
        Assert.AreEqual(0, transport.Rejected.Count);
    }

    [TestMethod]
    public async Task DeniedOutsider_IsRejectedOnlyOncePerSession()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        using var source = new TournamentSpectatorPolicySource();
        source.Replace(new TournamentSpectatorPolicy(Configuration()));
        var transport = new RecordingTransport();
        using var enforcement = new TournamentSpectatorEnforcement(
            events,
            players,
            new AssignmentSource(MatchId),
            source,
            transport);

        var current = await players.ConnectAsync(new PlayerConnection(
            Outsider, "Outsider", PlayerTeam.Spectator, true, Now));
        await players.UpdateAsync(new PlayerStateUpdate(
            Outsider,
            current.SessionId,
            "Outsider 2",
            PlayerTeam.Spectator,
            true,
            Now.AddSeconds(1)));

        Assert.AreEqual(1, transport.Rejected.Count);

        await players.ConnectAsync(new PlayerConnection(
            Outsider, "Replacement", PlayerTeam.Spectator, true, Now.AddSeconds(2)));
        Assert.AreEqual(2, transport.Rejected.Count);
    }

    [TestMethod]
    public async Task ReplacedDenyPolicy_CanRejectSameSessionAgain()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        using var source = new TournamentSpectatorPolicySource();
        source.Replace(new TournamentSpectatorPolicy(Configuration()));
        var transport = new RecordingTransport();
        using var enforcement = new TournamentSpectatorEnforcement(
            events,
            players,
            new AssignmentSource(MatchId),
            source,
            transport);

        await players.ConnectAsync(new PlayerConnection(
            Outsider, "Outsider", PlayerTeam.Spectator, true, Now));
        Assert.AreEqual(1, transport.Rejected.Count);

        source.Replace(new TournamentSpectatorPolicy(
            Configuration(), allowPublicSpectators: true));
        await enforcement.ReconcileOnlineAsync();
        Assert.AreEqual(1, transport.Rejected.Count);

        source.Replace(new TournamentSpectatorPolicy(Configuration()));
        await enforcement.ReconcileOnlineAsync();

        Assert.AreEqual(2, transport.Rejected.Count);
    }

    [TestMethod]
    public async Task PublicSpectatorMode_AllowsOtherwiseUnknownPlayers()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        using var source = new TournamentSpectatorPolicySource();
        source.Replace(new TournamentSpectatorPolicy(
            Configuration(), allowPublicSpectators: true));
        var transport = new RecordingTransport();
        using var enforcement = new TournamentSpectatorEnforcement(
            events,
            players,
            new AssignmentSource(MatchId),
            source,
            transport);

        await players.ConnectAsync(new PlayerConnection(
            Outsider, "Public", PlayerTeam.Terrorist, true, Now));

        Assert.AreEqual(1, transport.Spectator.Count);
        Assert.AreEqual(0, transport.Rejected.Count);
    }

    [TestMethod]
    public async Task PolicyReplacement_CancelsOldInFlightRejection()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        using var source = new TournamentSpectatorPolicySource();
        source.Replace(new TournamentSpectatorPolicy(Configuration()));
        var transport = new BlockingRejectTransport();
        using var enforcement = new TournamentSpectatorEnforcement(
            events,
            players,
            new AssignmentSource(MatchId),
            source,
            transport);

        var connect = players.ConnectAsync(new PlayerConnection(
            Outsider, "Outsider", PlayerTeam.Terrorist, true, Now)).AsTask();
        await transport.RejectStarted.Task;

        source.Replace(new TournamentSpectatorPolicy(
            Configuration(), allowPublicSpectators: true));
        await connect;

        Assert.IsTrue(transport.RejectCancelled);
        Assert.AreEqual(0, transport.RejectCompleted);

        await enforcement.ReconcileOnlineAsync();
        Assert.AreEqual(1, transport.SpectatorCompleted);
    }

    [TestMethod]
    public async Task StaleSessionEvent_CannotApplySpectatorPolicyToReplacement()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var first = await players.ConnectAsync(new PlayerConnection(
            Viewer, "Viewer", PlayerTeam.Terrorist, true, Now));
        var replacement = await players.ConnectAsync(new PlayerConnection(
            Viewer, "Replacement", PlayerTeam.Spectator, true, Now.AddSeconds(1)));

        using var source = new TournamentSpectatorPolicySource();
        source.Replace(new TournamentSpectatorPolicy(
            Configuration(), spectatorWhitelist: [Viewer]));
        var transport = new RecordingTransport();
        using var enforcement = new TournamentSpectatorEnforcement(
            events,
            players,
            new AssignmentSource(MatchId),
            source,
            transport);

        var stale = new PlayerSnapshot(
            first.Id,
            first.SessionId,
            first.Name,
            true,
            true,
            PlayerTeam.Terrorist,
            first.ConnectedAtUtc,
            Now.AddSeconds(2));
        await events.PublishAsync(new PlayerUpdatedEvent(first, stale));

        Assert.AreEqual(0, transport.Spectator.Count);
        Assert.IsTrue(players.TryGet(Viewer, out var current));
        Assert.AreEqual(replacement.SessionId, current?.SessionId);
    }

    [TestMethod]
    public async Task MismatchedActiveMatch_DoesNotApplyStalePolicy()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        using var source = new TournamentSpectatorPolicySource();
        source.Replace(new TournamentSpectatorPolicy(
            Configuration(), allowPublicSpectators: true));
        var transport = new RecordingTransport();
        using var enforcement = new TournamentSpectatorEnforcement(
            events,
            players,
            new AssignmentSource(Guid.NewGuid()),
            source,
            transport);

        await players.ConnectAsync(new PlayerConnection(
            Outsider, "Outsider", PlayerTeam.Terrorist, true, Now));

        Assert.AreEqual(0, transport.Spectator.Count);
        Assert.AreEqual(0, transport.Rejected.Count);
    }

    private static TournamentMatchConfiguration Configuration()
        => new(
            MatchId,
            TournamentBestOf.One,
            new TournamentTeam("Alpha", "A", A, [A]),
            new TournamentTeam("Beta", "B", B, [B]));

    private sealed class AssignmentSource(Guid matchId)
        : ITournamentTeamAssignmentSource
    {
        public Guid? ActiveMatchId { get; } = matchId;

        public bool TryGetAssignedSide(PlayerId playerId, out PlayerTeam side)
        {
            side = PlayerTeam.Unknown;
            return false;
        }
    }

    private sealed class RecordingTransport : ITournamentSpectatorTransport
    {
        public List<(PlayerId PlayerId, PlayerSessionId SessionId)> Spectator { get; } = [];
        public List<(PlayerId PlayerId, PlayerSessionId SessionId)> Rejected { get; } = [];

        public ValueTask MoveToSpectatorAsync(
            PlayerSnapshot player,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Spectator.Add((player.Id, player.SessionId));
            return ValueTask.CompletedTask;
        }

        public ValueTask RejectUnauthorizedAsync(
            PlayerSnapshot player,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Rejected.Add((player.Id, player.SessionId));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingRejectTransport : ITournamentSpectatorTransport
    {
        public TaskCompletionSource<bool> RejectStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool RejectCancelled { get; private set; }
        public int RejectCompleted { get; private set; }
        public int SpectatorCompleted { get; private set; }

        public ValueTask MoveToSpectatorAsync(
            PlayerSnapshot player,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SpectatorCompleted++;
            return ValueTask.CompletedTask;
        }

        public async ValueTask RejectUnauthorizedAsync(
            PlayerSnapshot player,
            CancellationToken cancellationToken = default)
        {
            RejectStarted.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                RejectCompleted++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                RejectCancelled = true;
                throw;
            }
        }
    }
}
