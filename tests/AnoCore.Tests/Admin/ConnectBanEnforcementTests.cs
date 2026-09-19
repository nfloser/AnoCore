using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Events;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ConnectBanEnforcementTests
{
    private static readonly PlayerId PlayerId = new(76561198000004001);
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 16, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CheckAsync_ExistingActiveSessionDisconnectsOnceAcrossRepeatedBootstrapChecks()
    {
        var events = new AnoEventBus();
        var moderation = new StubModerationService(ModerationRestriction.Connect);
        var disconnect = new StubDisconnectAction();
        using var enforcement = new ConnectBanEnforcement(
            events,
            moderation,
            disconnect,
            new FixedTimeProvider(Now));

        var player = Player(PlayerSessionId.New());
        await enforcement.CheckAsync(player);
        await enforcement.CheckAsync(player);

        Assert.AreEqual(2, moderation.StateCalls);
        Assert.AreEqual(1, disconnect.Calls);
    }

    [TestMethod]
    public async Task CheckAsync_ExistingUnrestrictedSessionIsAllowed()
    {
        var events = new AnoEventBus();
        var moderation = new StubModerationService(ModerationRestriction.None);
        var disconnect = new StubDisconnectAction();
        using var enforcement = new ConnectBanEnforcement(
            events,
            moderation,
            disconnect,
            new FixedTimeProvider(Now));

        await enforcement.CheckAsync(Player(PlayerSessionId.New()));

        Assert.AreEqual(1, moderation.StateCalls);
        Assert.AreEqual(0, disconnect.Calls);
    }

    [TestMethod]
    public async Task Connected_ActiveConnectRestrictionDisconnectsExactlyOncePerSession()
    {
        var events = new AnoEventBus();
        var moderation = new StubModerationService(ModerationRestriction.Connect);
        var disconnect = new StubDisconnectAction();
        using var enforcement = new ConnectBanEnforcement(
            events,
            moderation,
            disconnect,
            new FixedTimeProvider(Now));

        var player = Player(PlayerSessionId.New());
        await events.PublishAsync(new PlayerConnectedEvent(player));
        await events.PublishAsync(new PlayerConnectedEvent(player));

        Assert.AreEqual(2, moderation.StateCalls);
        Assert.AreEqual(1, disconnect.Calls);
        Assert.AreEqual(player.SessionId, disconnect.LastPlayer!.SessionId);
    }

    [TestMethod]
    public async Task Connected_ExpiredOrRevokedConnectRestrictionAllowsPlayer()
    {
        var events = new AnoEventBus();
        var moderation = new StubModerationService(ModerationRestriction.None);
        var disconnect = new StubDisconnectAction();
        using var enforcement = new ConnectBanEnforcement(
            events,
            moderation,
            disconnect,
            new FixedTimeProvider(Now));

        await events.PublishAsync(new PlayerConnectedEvent(Player(PlayerSessionId.New())));

        Assert.AreEqual(1, moderation.StateCalls);
        Assert.AreEqual(0, disconnect.Calls);
    }

    [TestMethod]
    public async Task Reconnected_NewSessionIsCheckedAndCanBeDisconnected()
    {
        var events = new AnoEventBus();
        var moderation = new StubModerationService(ModerationRestriction.Connect);
        var disconnect = new StubDisconnectAction();
        using var enforcement = new ConnectBanEnforcement(
            events,
            moderation,
            disconnect,
            new FixedTimeProvider(Now));

        var previous = Player(PlayerSessionId.New());
        var current = Player(PlayerSessionId.New());

        await events.PublishAsync(new PlayerConnectedEvent(previous));
        await events.PublishAsync(new PlayerReconnectedEvent(previous, current));

        Assert.AreEqual(2, moderation.StateCalls);
        Assert.AreEqual(2, disconnect.Calls);
        Assert.AreEqual(current.SessionId, disconnect.LastPlayer!.SessionId);
    }

    [TestMethod]
    public async Task DisconnectFailure_DoesNotConsumeSessionAndAllowsRetry()
    {
        var events = new AnoEventBus();
        var moderation = new StubModerationService(ModerationRestriction.Connect);
        var disconnect = new StubDisconnectAction { FailNext = true };
        using var enforcement = new ConnectBanEnforcement(
            events,
            moderation,
            disconnect,
            new FixedTimeProvider(Now));

        var player = Player(PlayerSessionId.New());

        await Assert.ThrowsExactlyAsync<AggregateException>(async () =>
            await events.PublishAsync(new PlayerConnectedEvent(player)));
        await events.PublishAsync(new PlayerConnectedEvent(player));

        Assert.AreEqual(2, disconnect.Calls);
    }

    [TestMethod]
    public async Task Cancellation_DoesNotInvokeDisconnectAndCanBeRetried()
    {
        var events = new AnoEventBus();
        var moderation = new StubModerationService(ModerationRestriction.Connect);
        var disconnect = new StubDisconnectAction();
        using var enforcement = new ConnectBanEnforcement(
            events,
            moderation,
            disconnect,
            new FixedTimeProvider(Now));

        var player = Player(PlayerSessionId.New());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await events.PublishAsync(new PlayerConnectedEvent(player), cancelled.Token));

        await events.PublishAsync(new PlayerConnectedEvent(player));

        Assert.AreEqual(1, disconnect.Calls);
    }

    [TestMethod]
    public async Task Disconnected_ReleasesTrackedSession()
    {
        var events = new AnoEventBus();
        var moderation = new StubModerationService(ModerationRestriction.Connect);
        var disconnect = new StubDisconnectAction();
        using var enforcement = new ConnectBanEnforcement(
            events,
            moderation,
            disconnect,
            new FixedTimeProvider(Now));

        var player = Player(PlayerSessionId.New());
        await events.PublishAsync(new PlayerConnectedEvent(player));
        await events.PublishAsync(new PlayerDisconnectedEvent(player));
        await events.PublishAsync(new PlayerConnectedEvent(player));

        Assert.AreEqual(2, disconnect.Calls);
    }

    [TestMethod]
    public async Task Dispose_UnsubscribesFromConnectionEvents()
    {
        var events = new AnoEventBus();
        var moderation = new StubModerationService(ModerationRestriction.Connect);
        var disconnect = new StubDisconnectAction();
        var enforcement = new ConnectBanEnforcement(
            events,
            moderation,
            disconnect,
            new FixedTimeProvider(Now));

        enforcement.Dispose();
        await events.PublishAsync(new PlayerConnectedEvent(Player(PlayerSessionId.New())));

        Assert.AreEqual(0, moderation.StateCalls);
        Assert.AreEqual(0, disconnect.Calls);
    }

    private static PlayerSnapshot Player(PlayerSessionId sessionId)
        => new(
            PlayerId,
            sessionId,
            "Banned Player",
            true,
            true,
            PlayerTeam.CounterTerrorist,
            Now,
            Now);

    private sealed class StubDisconnectAction : IPlayerDisconnectAction
    {
        public int Calls { get; private set; }

        public PlayerSnapshot? LastPlayer { get; private set; }

        public bool FailNext { get; set; }

        public ValueTask DisconnectAsync(
            PlayerSnapshot player,
            string reason,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastPlayer = player;
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("synthetic disconnect failure");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubModerationService(ModerationRestriction restrictions) : IModerationService
    {
        public int StateCalls { get; private set; }

        public ValueTask<ModerationState> GetStateAsync(
            PlayerId targetId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StateCalls++;
            return ValueTask.FromResult(new ModerationState(targetId, restrictions, []));
        }

        public ValueTask<IReadOnlyList<ModerationSanction>> ApplyAsync(
            PlayerId targetId,
            PlayerId? actorId,
            ModerationRestriction restrictions,
            string reason,
            DateTimeOffset atUtc,
            DateTimeOffset? expiresAtUtc = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ModerationSanction>> RevokeAsync(
            PlayerId targetId,
            PlayerId? actorId,
            ModerationRestriction restrictions,
            string reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ModerationSanction>> GetHistoryAsync(
            PlayerId targetId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ModerationAuditEntry>> GetAuditHistoryAsync(
            PlayerId targetId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
