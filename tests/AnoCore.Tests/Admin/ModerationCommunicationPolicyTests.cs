using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ModerationCommunicationPolicyTests
{
    private static readonly PlayerId Player = new(76561198000005001);
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 18, 45, 0, TimeSpan.Zero);

    [TestMethod]
    public void Evaluate_ReturnsSnapshotUnavailableOnCacheMiss()
    {
        var snapshots = new StubSnapshots { IsLoaded = false };
        var policy = new ModerationCommunicationPolicy(
            snapshots,
            new FixedTimeProvider(Now));

        Assert.AreEqual(
            CommunicationRestrictionDecision.SnapshotUnavailable,
            policy.Evaluate(Player, CommunicationChannel.Chat));
        Assert.AreEqual(Now, snapshots.LastAtUtc);
    }

    [TestMethod]
    public void Evaluate_AllowsUnrestrictedPlayer()
    {
        var policy = Policy(ModerationRestriction.None);

        Assert.AreEqual(
            CommunicationRestrictionDecision.Allowed,
            policy.Evaluate(Player, CommunicationChannel.Chat));
        Assert.AreEqual(
            CommunicationRestrictionDecision.Allowed,
            policy.Evaluate(Player, CommunicationChannel.Voice));
    }

    [TestMethod]
    public void Evaluate_ChatRestrictionBlocksOnlyChat()
    {
        var policy = Policy(ModerationRestriction.Chat);

        Assert.AreEqual(
            CommunicationRestrictionDecision.Blocked,
            policy.Evaluate(Player, CommunicationChannel.Chat));
        Assert.AreEqual(
            CommunicationRestrictionDecision.Allowed,
            policy.Evaluate(Player, CommunicationChannel.Voice));
    }

    [TestMethod]
    public void Evaluate_VoiceRestrictionBlocksOnlyVoice()
    {
        var policy = Policy(ModerationRestriction.Voice);

        Assert.AreEqual(
            CommunicationRestrictionDecision.Allowed,
            policy.Evaluate(Player, CommunicationChannel.Chat));
        Assert.AreEqual(
            CommunicationRestrictionDecision.Blocked,
            policy.Evaluate(Player, CommunicationChannel.Voice));
    }

    [TestMethod]
    public void Evaluate_SilenceBlocksBothChannels()
    {
        var policy = Policy(ModerationRestriction.Chat | ModerationRestriction.Voice);

        Assert.AreEqual(
            CommunicationRestrictionDecision.Blocked,
            policy.Evaluate(Player, CommunicationChannel.Chat));
        Assert.AreEqual(
            CommunicationRestrictionDecision.Blocked,
            policy.Evaluate(Player, CommunicationChannel.Voice));
    }

    [TestMethod]
    public void Evaluate_UsesCurrentUtcForSnapshotExpiry()
    {
        var expiresAt = Now.AddMinutes(5);
        var snapshots = new StubSnapshots
        {
            IsLoaded = true,
            Restrictions = ModerationRestriction.Chat,
            ExpiresAtUtc = expiresAt,
        };
        var time = new MutableTimeProvider(expiresAt.AddTicks(-1));
        var policy = new ModerationCommunicationPolicy(snapshots, time);

        Assert.AreEqual(
            CommunicationRestrictionDecision.Blocked,
            policy.Evaluate(Player, CommunicationChannel.Chat));

        time.UtcNow = expiresAt;

        Assert.AreEqual(
            CommunicationRestrictionDecision.Allowed,
            policy.Evaluate(Player, CommunicationChannel.Chat));
        Assert.AreEqual(expiresAt, snapshots.LastAtUtc);
    }

    [TestMethod]
    public void Evaluate_RejectsUnknownCommunicationChannel()
    {
        var policy = Policy(ModerationRestriction.None);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            policy.Evaluate(Player, (CommunicationChannel)999));
    }

    private static ModerationCommunicationPolicy Policy(ModerationRestriction restrictions)
        => new(
            new StubSnapshots
            {
                IsLoaded = true,
                Restrictions = restrictions,
            },
            new FixedTimeProvider(Now));

    private sealed class StubSnapshots : IModerationSnapshotProvider
    {
        public bool IsLoaded { get; init; }

        public ModerationRestriction Restrictions { get; init; }

        public DateTimeOffset? ExpiresAtUtc { get; init; }

        public DateTimeOffset? LastAtUtc { get; private set; }

        public bool TryGetRestrictions(
            PlayerId targetId,
            DateTimeOffset atUtc,
            out ModerationRestriction restrictions)
        {
            LastAtUtc = atUtc;
            restrictions = ExpiresAtUtc is { } expires && atUtc >= expires
                ? ModerationRestriction.None
                : Restrictions;
            return IsLoaded;
        }

        public ValueTask InvalidateAsync(
            PlayerId targetId,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
