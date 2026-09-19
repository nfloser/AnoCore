using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Moderation;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ModerationSnapshotLifecycleTests
{
    private static readonly PlayerId Player = new(76561198000006001);
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 19, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ConnectedPlayer_WarmsSnapshotAtCurrentUtc()
    {
        var events = new AnoEventBus();
        var moderation = new RecordingModeration();
        using var lifecycle = new ModerationSnapshotLifecycle(
            events,
            moderation,
            moderation,
            new FixedTimeProvider(Now));

        var connected = Snapshot(PlayerSessionId.New(), isConnected: true);
        await events.PublishAsync(new PlayerConnectedEvent(connected));

        Assert.AreEqual(1, moderation.Warms.Count);
        Assert.AreEqual(Player, moderation.Warms[0].PlayerId);
        Assert.AreEqual(Now, moderation.Warms[0].AtUtc);
        Assert.AreEqual(0, moderation.Invalidations.Count);
    }

    [TestMethod]
    public async Task WarmExistingAsync_TracksAlreadyOnlineSessionAndInvalidatesOnDisconnect()
    {
        var events = new AnoEventBus();
        var moderation = new RecordingModeration();
        using var lifecycle = new ModerationSnapshotLifecycle(
            events,
            moderation,
            moderation,
            new FixedTimeProvider(Now));

        var existing = Snapshot(PlayerSessionId.New(), isConnected: true);
        await lifecycle.WarmExistingAsync([existing]);

        Assert.AreEqual(1, moderation.Warms.Count);
        Assert.AreEqual(Player, moderation.Warms.Single().PlayerId);

        moderation.ResetCalls();
        await events.PublishAsync(new PlayerDisconnectedEvent(
            Snapshot(existing.SessionId, isConnected: false)));

        Assert.AreEqual(1, moderation.Invalidations.Count);
        Assert.AreEqual(Player, moderation.Invalidations.Single());
    }

    [TestMethod]
    public async Task Reconnect_InvalidatesPreviousSnapshotThenWarmsCurrentSession()
    {
        var events = new AnoEventBus();
        var moderation = new RecordingModeration();
        using var lifecycle = new ModerationSnapshotLifecycle(
            events,
            moderation,
            moderation,
            new FixedTimeProvider(Now));

        var previous = Snapshot(PlayerSessionId.New(), isConnected: true);
        await events.PublishAsync(new PlayerConnectedEvent(previous));

        moderation.ResetCalls();

        var current = Snapshot(PlayerSessionId.New(), isConnected: true);
        await events.PublishAsync(new PlayerReconnectedEvent(previous, current));

        CollectionAssert.AreEqual(
            new[] { "invalidate", "warm" },
            moderation.CallOrder.ToArray());
        Assert.AreEqual(Player, moderation.Invalidations.Single());
        Assert.AreEqual(Player, moderation.Warms.Single().PlayerId);
    }

    [TestMethod]
    public async Task StaleDisconnectAfterReconnect_DoesNotInvalidateCurrentSnapshot()
    {
        var events = new AnoEventBus();
        var moderation = new RecordingModeration();
        using var lifecycle = new ModerationSnapshotLifecycle(
            events,
            moderation,
            moderation,
            new FixedTimeProvider(Now));

        var previous = Snapshot(PlayerSessionId.New(), isConnected: true);
        await events.PublishAsync(new PlayerConnectedEvent(previous));

        var current = Snapshot(PlayerSessionId.New(), isConnected: true);
        await events.PublishAsync(new PlayerReconnectedEvent(previous, current));

        moderation.ResetCalls();

        await events.PublishAsync(new PlayerDisconnectedEvent(
            Snapshot(previous.SessionId, isConnected: false)));

        Assert.AreEqual(0, moderation.Invalidations.Count);
    }

    [TestMethod]
    public async Task CurrentDisconnect_InvalidatesSnapshotAndForgetsSession()
    {
        var events = new AnoEventBus();
        var moderation = new RecordingModeration();
        using var lifecycle = new ModerationSnapshotLifecycle(
            events,
            moderation,
            moderation,
            new FixedTimeProvider(Now));

        var current = Snapshot(PlayerSessionId.New(), isConnected: true);
        await events.PublishAsync(new PlayerConnectedEvent(current));
        moderation.ResetCalls();

        var disconnected = Snapshot(current.SessionId, isConnected: false);
        await events.PublishAsync(new PlayerDisconnectedEvent(disconnected));
        await events.PublishAsync(new PlayerDisconnectedEvent(disconnected));

        Assert.AreEqual(1, moderation.Invalidations.Count);
        Assert.AreEqual(Player, moderation.Invalidations.Single());
    }

    [TestMethod]
    public async Task Dispose_UnsubscribesFromLifecycleEvents()
    {
        var events = new AnoEventBus();
        var moderation = new RecordingModeration();
        var lifecycle = new ModerationSnapshotLifecycle(
            events,
            moderation,
            moderation,
            new FixedTimeProvider(Now));

        lifecycle.Dispose();

        await events.PublishAsync(new PlayerConnectedEvent(
            Snapshot(PlayerSessionId.New(), isConnected: true)));

        Assert.AreEqual(0, moderation.Warms.Count);
        Assert.AreEqual(0, moderation.Invalidations.Count);
    }

    [TestMethod]
    public async Task DisconnectDuringWarm_LeavesSnapshotInvalidated()
    {
        var events = new AnoEventBus();
        var repository = new BlockingRepository(
            new ModerationSanction(
                Guid.NewGuid(),
                Player,
                null,
                ModerationRestriction.Chat,
                "cached",
                Now));
        var moderation = new ModerationService(repository);
        using var lifecycle = new ModerationSnapshotLifecycle(
            events,
            moderation,
            moderation,
            new FixedTimeProvider(Now));

        var current = Snapshot(PlayerSessionId.New(), isConnected: true);
        var connect = events.PublishAsync(new PlayerConnectedEvent(current)).AsTask();
        await repository.ReadStarted.Task;

        var disconnect = events.PublishAsync(new PlayerDisconnectedEvent(
            Snapshot(current.SessionId, isConnected: false))).AsTask();

        Assert.IsFalse(disconnect.IsCompleted);

        repository.ReleaseRead.TrySetResult();
        await connect;
        await disconnect;

        Assert.IsFalse(((IModerationSnapshotProvider)moderation).TryGetRestrictions(
            Player,
            Now,
            out _));
    }

    [TestMethod]
    public async Task WarmFailure_DoesNotCreateAvailableSnapshot()
    {
        var events = new AnoEventBus();
        var moderation = new ModerationService(new FailingRepository());
        using var lifecycle = new ModerationSnapshotLifecycle(
            events,
            moderation,
            moderation,
            new FixedTimeProvider(Now));

        await Assert.ThrowsExactlyAsync<AggregateException>(async () =>
            await events.PublishAsync(new PlayerConnectedEvent(
                Snapshot(PlayerSessionId.New(), isConnected: true))));

        Assert.IsFalse(((IModerationSnapshotProvider)moderation).TryGetRestrictions(
            Player,
            Now,
            out _));
    }

    [TestMethod]
    public async Task Dispose_CancelsInflightWarmWithoutPublishingFailure()
    {
        var events = new AnoEventBus();
        var repository = new BlockingRepository();
        var moderation = new ModerationService(repository);
        var lifecycle = new ModerationSnapshotLifecycle(
            events,
            moderation,
            moderation,
            new FixedTimeProvider(Now));

        var connect = events.PublishAsync(new PlayerConnectedEvent(
            Snapshot(PlayerSessionId.New(), isConnected: true))).AsTask();
        await repository.ReadStarted.Task;

        lifecycle.Dispose();
        await connect;

        Assert.IsFalse(((IModerationSnapshotProvider)moderation).TryGetRestrictions(
            Player,
            Now,
            out _));
    }

    private static PlayerSnapshot Snapshot(PlayerSessionId sessionId, bool isConnected)
        => new(
            Player,
            sessionId,
            "Lifecycle Player",
            isConnected,
            isAlive: isConnected,
            PlayerTeam.CounterTerrorist,
            Now,
            Now);

    private sealed class BlockingRepository(params ModerationSanction[] sanctions) : IModerationRepository
    {
        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IReadOnlyList<ModerationSanction>> GetActiveAsync(
            PlayerId targetId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await ReleaseRead.Task.WaitAsync(cancellationToken);
            return sanctions
                .Where(value => value.TargetId == targetId && value.IsActiveAt(atUtc))
                .ToArray();
        }

        public ValueTask AddAsync(
            IReadOnlyCollection<ModerationSanction> values,
            ModerationAuditEntry audit,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ModerationSanction>> GetHistoryAsync(
            PlayerId targetId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ModerationSanction>> RevokeActiveAsync(
            PlayerId targetId,
            ModerationRestriction restrictions,
            PlayerId? actorId,
            string reason,
            DateTimeOffset atUtc,
            ModerationAuditEntry audit,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ModerationAuditEntry>> GetAuditHistoryAsync(
            PlayerId targetId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FailingRepository : IModerationRepository
    {
        public ValueTask<IReadOnlyList<ModerationSanction>> GetActiveAsync(
            PlayerId targetId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated warm failure.");

        public ValueTask AddAsync(
            IReadOnlyCollection<ModerationSanction> sanctions,
            ModerationAuditEntry audit,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ModerationSanction>> GetHistoryAsync(
            PlayerId targetId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ModerationSanction>> RevokeActiveAsync(
            PlayerId targetId,
            ModerationRestriction restrictions,
            PlayerId? actorId,
            string reason,
            DateTimeOffset atUtc,
            ModerationAuditEntry audit,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ModerationAuditEntry>> GetAuditHistoryAsync(
            PlayerId targetId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingModeration : IModerationService, IModerationSnapshotProvider
    {
        public List<(PlayerId PlayerId, DateTimeOffset AtUtc)> Warms { get; } = [];

        public List<PlayerId> Invalidations { get; } = [];

        public List<string> CallOrder { get; } = [];

        public ValueTask<ModerationState> GetStateAsync(
            PlayerId targetId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Warms.Add((targetId, atUtc));
            CallOrder.Add("warm");
            return ValueTask.FromResult(new ModerationState(
                targetId,
                ModerationRestriction.None,
                []));
        }

        public ValueTask InvalidateAsync(
            PlayerId targetId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invalidations.Add(targetId);
            CallOrder.Add("invalidate");
            return ValueTask.CompletedTask;
        }

        public bool TryGetRestrictions(
            PlayerId targetId,
            DateTimeOffset atUtc,
            out ModerationRestriction restrictions)
        {
            restrictions = ModerationRestriction.None;
            return Warms.Any(value => value.PlayerId == targetId)
                && !Invalidations.Contains(targetId);
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

        public void ResetCalls()
        {
            Warms.Clear();
            Invalidations.Clear();
            CallOrder.Clear();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
