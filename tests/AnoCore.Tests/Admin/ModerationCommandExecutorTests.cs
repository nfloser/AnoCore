using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ModerationCommandExecutorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 14, 0, 0, TimeSpan.Zero);
    private static readonly PlayerId Actor = new(76561198000003101);
    private static readonly PlayerId Target = new(76561198000003102);

    [TestMethod]
    public async Task ExecuteAsync_PermanentBanUsesCentralTargetGatewayAndAppliesConnectRestriction()
    {
        var targets = new StubTargetGateway(Target);
        var moderation = new StubModerationService();
        var executor = new ModerationCommandExecutor(targets, moderation, new FixedTimeProvider(Now));

        var result = await executor.ExecuteAsync(
            ModerationAdminOperation.Ban,
            Actor,
            Target.SteamId64.ToString(),
            0,
            null);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(new PermissionId("ano.admin.ban"), targets.LastPermission);
        Assert.AreEqual(1, moderation.ApplyCalls);
        Assert.AreEqual(0, moderation.RevokeCalls);
        Assert.AreEqual(Target, moderation.LastTarget);
        Assert.AreEqual(Actor, moderation.LastActor);
        Assert.AreEqual(ModerationRestriction.Connect, moderation.LastRestrictions);
        Assert.AreEqual(ModerationCommandExecutor.DefaultReason, moderation.LastReason);
        Assert.AreEqual(Now, moderation.LastAtUtc);
        Assert.IsNull(moderation.LastExpiresAtUtc);
    }

    [TestMethod]
    public async Task ExecuteAsync_TemporarySilenceAppliesVoiceAndChatWithExactExpiry()
    {
        var targets = new StubTargetGateway(Target);
        var moderation = new StubModerationService();
        var executor = new ModerationCommandExecutor(targets, moderation, new FixedTimeProvider(Now));

        var result = await executor.ExecuteAsync(
            ModerationAdminOperation.Silence,
            Actor,
            "Target",
            15,
            "  repeated abuse  ");

        Assert.IsTrue(result.Success);
        Assert.AreEqual(new PermissionId("ano.admin.silence"), targets.LastPermission);
        Assert.AreEqual(ModerationRestriction.Voice | ModerationRestriction.Chat, moderation.LastRestrictions);
        Assert.AreEqual("repeated abuse", moderation.LastReason);
        Assert.AreEqual(Now.AddMinutes(15), moderation.LastExpiresAtUtc);
    }

    [TestMethod]
    public async Task ExecuteAsync_RejectsNegativeOrOverflowingDurationBeforeTargetLookup()
    {
        var targets = new StubTargetGateway(Target);
        var moderation = new StubModerationService();
        var executor = new ModerationCommandExecutor(
            targets,
            moderation,
            new FixedTimeProvider(DateTimeOffset.MaxValue.AddMinutes(-1)));

        var negative = await executor.ExecuteAsync(
            ModerationAdminOperation.Mute,
            Actor,
            "Target",
            -1,
            "bad duration");
        var overflow = await executor.ExecuteAsync(
            ModerationAdminOperation.Mute,
            Actor,
            "Target",
            int.MaxValue,
            "bad duration");

        Assert.IsFalse(negative.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, negative.FailureReason);
        Assert.IsFalse(overflow.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, overflow.FailureReason);
        Assert.AreEqual(0, targets.Calls);
        Assert.AreEqual(0, moderation.ApplyCalls);
    }

    [TestMethod]
    public async Task ExecuteAsync_UnmuteRevokesVoiceAndReportsMissingActiveRestriction()
    {
        var targets = new StubTargetGateway(Target);
        var moderation = new StubModerationService
        {
            RevokeResult = [],
        };
        var executor = new ModerationCommandExecutor(targets, moderation, new FixedTimeProvider(Now));

        var missing = await executor.ExecuteAsync(
            ModerationAdminOperation.Unmute,
            Actor,
            "Target",
            null,
            "manual clear");

        Assert.IsFalse(missing.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, missing.FailureReason);
        Assert.AreEqual(new PermissionId("ano.admin.unmute"), targets.LastPermission);
        Assert.AreEqual(1, moderation.RevokeCalls);
        Assert.AreEqual(0, moderation.ApplyCalls);
        Assert.AreEqual(ModerationRestriction.Voice, moderation.LastRestrictions);
        Assert.AreEqual("manual clear", moderation.LastReason);
    }

    [TestMethod]
    public async Task ExecuteAsync_TargetPermissionFailureMapsToForbiddenWithoutModerationMutation()
    {
        var targets = new StubTargetGateway(Target)
        {
            Result = ModerationTargetResult.Reject(ModerationTargetFailure.TargetImmune),
        };
        var moderation = new StubModerationService();
        var executor = new ModerationCommandExecutor(targets, moderation, new FixedTimeProvider(Now));

        var result = await executor.ExecuteAsync(
            ModerationAdminOperation.Gag,
            Actor,
            "Target",
            30,
            "spam");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.AreEqual(0, moderation.ApplyCalls);
        Assert.AreEqual(0, moderation.RevokeCalls);
    }

    [TestMethod]
    public async Task ExecuteAsync_AmbiguousOrMissingTargetMapsToInvalidInput()
    {
        var targets = new StubTargetGateway(Target);
        var moderation = new StubModerationService();
        var executor = new ModerationCommandExecutor(targets, moderation, new FixedTimeProvider(Now));

        targets.Result = ModerationTargetResult.Reject(ModerationTargetFailure.Ambiguous);
        var ambiguous = await executor.ExecuteAsync(
            ModerationAdminOperation.Ban,
            Actor,
            "ali",
            10,
            "reason");

        targets.Result = ModerationTargetResult.Reject(ModerationTargetFailure.NotFound);
        var missing = await executor.ExecuteAsync(
            ModerationAdminOperation.Ban,
            Actor,
            "missing",
            10,
            "reason");

        Assert.AreEqual(CommandFailureReason.InvalidInput, ambiguous.FailureReason);
        Assert.AreEqual(CommandFailureReason.InvalidInput, missing.FailureReason);
        Assert.AreEqual(0, moderation.ApplyCalls);
    }

    private sealed class StubTargetGateway(PlayerId targetId) : IModerationTargetGateway
    {
        public ModerationTargetResult Result { get; set; } =
            ModerationTargetResult.Success(targetId);

        public int Calls { get; private set; }

        public PermissionId? LastPermission { get; private set; }

        public ValueTask<ModerationTargetResult> ResolveAsync(
            string selector,
            PlayerId? actor,
            PermissionId permission,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastPermission = permission;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class StubModerationService : IModerationService
    {
        public int ApplyCalls { get; private set; }

        public int RevokeCalls { get; private set; }

        public PlayerId? LastTarget { get; private set; }

        public PlayerId? LastActor { get; private set; }

        public ModerationRestriction LastRestrictions { get; private set; }

        public string? LastReason { get; private set; }

        public DateTimeOffset LastAtUtc { get; private set; }

        public DateTimeOffset? LastExpiresAtUtc { get; private set; }

        public IReadOnlyList<ModerationSanction>? RevokeResult { get; set; }

        public ValueTask<IReadOnlyList<ModerationSanction>> ApplyAsync(
            PlayerId targetId,
            PlayerId? actorId,
            ModerationRestriction restrictions,
            string reason,
            DateTimeOffset atUtc,
            DateTimeOffset? expiresAtUtc = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCalls++;
            Capture(targetId, actorId, restrictions, reason, atUtc, expiresAtUtc);
            IReadOnlyList<ModerationSanction> result =
            [
                new(
                    Guid.NewGuid(),
                    targetId,
                    actorId,
                    restrictions.HasFlag(ModerationRestriction.Connect)
                        ? ModerationRestriction.Connect
                        : restrictions.HasFlag(ModerationRestriction.Voice)
                            ? ModerationRestriction.Voice
                            : ModerationRestriction.Chat,
                    reason,
                    atUtc,
                    expiresAtUtc),
            ];
            return ValueTask.FromResult(result);
        }

        public ValueTask<IReadOnlyList<ModerationSanction>> RevokeAsync(
            PlayerId targetId,
            PlayerId? actorId,
            ModerationRestriction restrictions,
            string reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RevokeCalls++;
            Capture(targetId, actorId, restrictions, reason, atUtc, null);
            return ValueTask.FromResult(
                RevokeResult
                ?? (IReadOnlyList<ModerationSanction>)
                [
                    new ModerationSanction(
                        Guid.NewGuid(),
                        targetId,
                        actorId,
                        restrictions.HasFlag(ModerationRestriction.Connect)
                            ? ModerationRestriction.Connect
                            : restrictions.HasFlag(ModerationRestriction.Voice)
                                ? ModerationRestriction.Voice
                                : ModerationRestriction.Chat,
                        reason,
                        atUtc).Revoke(actorId, reason, atUtc.AddTicks(1)),
                ]);
        }

        public ValueTask<ModerationState> GetStateAsync(
            PlayerId targetId,
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

        private void Capture(
            PlayerId targetId,
            PlayerId? actorId,
            ModerationRestriction restrictions,
            string reason,
            DateTimeOffset atUtc,
            DateTimeOffset? expiresAtUtc)
        {
            LastTarget = targetId;
            LastActor = actorId;
            LastRestrictions = restrictions;
            LastReason = reason;
            LastAtUtc = atUtc;
            LastExpiresAtUtc = expiresAtUtc;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
