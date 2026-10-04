using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Modules.Tournament;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentTeamEnforcementTests
{
    private static readonly PlayerId Player = new(76561198000195001);
    private static readonly PlayerId Outsider = new(76561198000195999);
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ConnectAndReconnect_EnforceAssignedSideOnlyWhenNeeded()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var assignments = new AssignmentSource(Player, PlayerTeam.CounterTerrorist);
        var transport = new RecordingTransport();
        using var enforcement = new TournamentTeamEnforcement(
            events, players, assignments, transport);

        var first = await players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.Terrorist, true, Now));
        Assert.AreEqual(1, transport.Calls.Count);
        Assert.AreEqual(first.SessionId, transport.Calls[0].SessionId);
        Assert.AreEqual(PlayerTeam.CounterTerrorist, transport.Calls[0].Team);

        transport.Calls.Clear();
        await players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.CounterTerrorist, true, Now.AddSeconds(1)));
        Assert.AreEqual(0, transport.Calls.Count);
    }

    [TestMethod]
    public async Task OutsiderAndMatchingTeam_AreIgnored()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var transport = new RecordingTransport();
        using var enforcement = new TournamentTeamEnforcement(
            events,
            players,
            new AssignmentSource(Player, PlayerTeam.Terrorist),
            transport);

        await players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.Terrorist, true, Now));
        await players.ConnectAsync(new PlayerConnection(
            Outsider, "Outsider", PlayerTeam.CounterTerrorist, true, Now));

        Assert.AreEqual(0, transport.Calls.Count);
    }

    [TestMethod]
    public async Task EnforcedRegistryUpdate_DoesNotLoopBackIntoSecondTransportCall()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var transport = new UpdatingTransport(players);
        using var enforcement = new TournamentTeamEnforcement(
            events,
            players,
            new AssignmentSource(Player, PlayerTeam.CounterTerrorist),
            transport);

        var connected = await players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.Terrorist, true, Now));

        Assert.AreEqual(1, transport.Calls);
        Assert.IsTrue(players.TryGet(Player, out var current));
        Assert.IsNotNull(current);
        Assert.AreEqual(connected.SessionId, current.SessionId);
        Assert.AreEqual(PlayerTeam.CounterTerrorist, current.Team);
    }

    [TestMethod]
    public async Task StaleSessionEvent_CannotMoveReplacementSession()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var first = await players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.Terrorist, true, Now));
        var current = await players.ConnectAsync(new PlayerConnection(
            Player, "Replacement", PlayerTeam.CounterTerrorist, true, Now.AddSeconds(1)));

        var transport = new RecordingTransport();
        using var enforcement = new TournamentTeamEnforcement(
            events,
            players,
            new AssignmentSource(Player, PlayerTeam.CounterTerrorist),
            transport);

        var stale = new PlayerSnapshot(
            first.Id,
            first.SessionId,
            first.Name,
            true,
            first.IsAlive,
            PlayerTeam.Terrorist,
            first.ConnectedAtUtc,
            Now.AddSeconds(2));
        await events.PublishAsync(new PlayerUpdatedEvent(first, stale));

        Assert.AreEqual(0, transport.Calls.Count);
        Assert.IsTrue(players.TryGet(Player, out var after));
        Assert.AreEqual(current.SessionId, after?.SessionId);
    }

    [TestMethod]
    public async Task Dispose_CancelsScheduledEnforcementAndUnsubscribes()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var transport = new BlockingTransport();
        var enforcement = new TournamentTeamEnforcement(
            events,
            players,
            new AssignmentSource(Player, PlayerTeam.CounterTerrorist),
            transport);

        var connect = players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.Terrorist, true, Now)).AsTask();
        await transport.Started.Task;
        enforcement.Dispose();
        await connect;

        Assert.IsTrue(transport.Cancelled);

        await players.ConnectAsync(new PlayerConnection(
            Player, "Again", PlayerTeam.Terrorist, true, Now.AddSeconds(3)));
        Assert.AreEqual(1, transport.CallCount);
    }

    [TestMethod]
    public async Task ReconcileOnline_RepairsAlreadyConnectedRosterPlayers()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var current = await players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.Terrorist, true, Now));

        var transport = new RecordingTransport();
        using var enforcement = new TournamentTeamEnforcement(
            events,
            players,
            new AssignmentSource(Player, PlayerTeam.CounterTerrorist),
            transport);

        await enforcement.ReconcileOnlineAsync();

        Assert.AreEqual(1, transport.Calls.Count);
        Assert.AreEqual(current.SessionId, transport.Calls[0].SessionId);
    }

    private sealed class AssignmentSource : ITournamentTeamAssignmentSource
    {
        private readonly PlayerId _player;
        private readonly PlayerTeam _team;

        public AssignmentSource(PlayerId player, PlayerTeam team)
        {
            _player = player;
            _team = team;
        }

        public Guid? ActiveMatchId => Guid.Parse("11111111-1111-1111-1111-111111111111");

        public bool TryGetAssignedSide(PlayerId playerId, out PlayerTeam side)
        {
            if (playerId == _player)
            {
                side = _team;
                return true;
            }

            side = PlayerTeam.Unknown;
            return false;
        }
    }

    private sealed class RecordingTransport : ITournamentTeamTransport
    {
        public List<(PlayerSessionId SessionId, PlayerTeam Team)> Calls { get; } = [];

        public ValueTask SetTeamAsync(
            PlayerSnapshot player,
            PlayerTeam team,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((player.SessionId, team));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UpdatingTransport(PlayerRegistry players) : ITournamentTeamTransport
    {
        public int Calls { get; private set; }

        public async ValueTask SetTeamAsync(
            PlayerSnapshot player,
            PlayerTeam team,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            await players.UpdateAsync(
                new PlayerStateUpdate(
                    player.Id,
                    player.SessionId,
                    null,
                    team,
                    null,
                    player.LastUpdatedAtUtc.AddTicks(1)),
                cancellationToken);
        }
    }

    private sealed class BlockingTransport : ITournamentTeamTransport
    {
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }
        public bool Cancelled { get; private set; }

        public async ValueTask SetTeamAsync(
            PlayerSnapshot player,
            PlayerTeam team,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled = true;
                throw;
            }
        }
    }
}
