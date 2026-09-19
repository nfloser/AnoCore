using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Moderation;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ModerationChatRuntimeTests
{
    private static readonly PlayerId Player = new(76561198000007011);
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 19, 45, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Connect_WarmsPersistedChatRestrictionAndDisconnectInvalidatesIt()
    {
        var repository = new ReadOnlyRepository(
            new ModerationSanction(
                Guid.NewGuid(),
                Player,
                null,
                ModerationRestriction.Chat,
                "persisted gag",
                Now));
        var moderation = new ModerationService(repository);
        var events = new AnoEventBus();

        using var runtime = new ModerationChatRuntime(
            events,
            moderation,
            moderation,
            new FixedTimeProvider(Now));

        Assert.AreEqual(ChatInterceptionDecision.Block, runtime.Gate.Evaluate(Player));
        Assert.IsFalse(((IModerationSnapshotProvider)moderation).TryGetRestrictions(Player, Now, out _));

        var connected = Snapshot(PlayerSessionId.New(), isConnected: true);
        await events.PublishAsync(new PlayerConnectedEvent(connected));

        Assert.IsTrue(((IModerationSnapshotProvider)moderation).TryGetRestrictions(
            Player,
            Now,
            out var restrictions));
        Assert.AreEqual(ModerationRestriction.Chat, restrictions);
        Assert.AreEqual(ChatInterceptionDecision.Block, runtime.Gate.Evaluate(Player));

        await events.PublishAsync(new PlayerDisconnectedEvent(
            Snapshot(connected.SessionId, isConnected: false)));

        Assert.IsFalse(((IModerationSnapshotProvider)moderation).TryGetRestrictions(Player, Now, out _));
        Assert.AreEqual(ChatInterceptionDecision.Block, runtime.Gate.Evaluate(Player));
    }

    [TestMethod]
    public async Task Connect_WarmsUnrestrictedPlayerAndAllowsChat()
    {
        var moderation = new ModerationService(new ReadOnlyRepository());
        var events = new AnoEventBus();

        using var runtime = new ModerationChatRuntime(
            events,
            moderation,
            moderation,
            new FixedTimeProvider(Now));

        await events.PublishAsync(new PlayerConnectedEvent(
            Snapshot(PlayerSessionId.New(), isConnected: true)));

        Assert.AreEqual(ChatInterceptionDecision.Allow, runtime.Gate.Evaluate(Player));
    }

    private static PlayerSnapshot Snapshot(PlayerSessionId sessionId, bool isConnected)
        => new(
            Player,
            sessionId,
            "Chat Runtime Player",
            isConnected,
            isAlive: isConnected,
            PlayerTeam.CounterTerrorist,
            Now,
            Now);

    private sealed class ReadOnlyRepository(params ModerationSanction[] sanctions) : IModerationRepository
    {
        public ValueTask<IReadOnlyList<ModerationSanction>> GetActiveAsync(
            PlayerId targetId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ModerationSanction> result = sanctions
                .Where(value => value.TargetId == targetId && value.IsActiveAt(atUtc))
                .ToArray();
            return ValueTask.FromResult(result);
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
