using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Moderation;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ModerationCommunicationRuntimeTests
{
    private static readonly PlayerId Player = new(76561198000007011);
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 19, 45, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task WarmExistingAsync_WarmsPersistedChatRestrictionAndTracksDisconnect()
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

        using var runtime = new ModerationCommunicationRuntime(
            events,
            moderation,
            moderation,
            new FixedTimeProvider(Now));

        var existing = Snapshot(PlayerSessionId.New(), isConnected: true);
        await runtime.WarmExistingAsync([existing]);

        Assert.IsTrue(((IModerationSnapshotProvider)moderation).TryGetRestrictions(
            Player,
            Now,
            out var restrictions));
        Assert.AreEqual(ModerationRestriction.Chat, restrictions);
        Assert.AreEqual(ChatInterceptionDecision.Block, runtime.ChatGate.Evaluate(Player));
        Assert.AreEqual(VoiceInterceptionDecision.Allow, runtime.VoiceGate.Evaluate(Player));

        await events.PublishAsync(new PlayerDisconnectedEvent(
            Snapshot(existing.SessionId, isConnected: false)));

        Assert.IsFalse(((IModerationSnapshotProvider)moderation).TryGetRestrictions(Player, Now, out _));
        Assert.AreEqual(ChatInterceptionDecision.Block, runtime.ChatGate.Evaluate(Player));
    }

    [TestMethod]
    public async Task ConnectedPlayer_WarmsUnrestrictedPlayerAndAllowsChat()
    {
        var moderation = new ModerationService(new ReadOnlyRepository());
        var events = new AnoEventBus();

        using var runtime = new ModerationCommunicationRuntime(
            events,
            moderation,
            moderation,
            new FixedTimeProvider(Now));

        await events.PublishAsync(new PlayerConnectedEvent(
            Snapshot(PlayerSessionId.New(), isConnected: true)));

        Assert.AreEqual(ChatInterceptionDecision.Allow, runtime.ChatGate.Evaluate(Player));
        Assert.AreEqual(VoiceInterceptionDecision.Allow, runtime.VoiceGate.Evaluate(Player));
    }

    private static PlayerSnapshot Snapshot(PlayerSessionId sessionId, bool isConnected)
        => new(
            Player,
            sessionId,
            "Communication Runtime Player",
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
