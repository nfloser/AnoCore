using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Warnings;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Commands;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class WarningCommandTests
{
    private static readonly PlayerId Actor = new(76561198000009701);
    private static readonly PlayerId Target = new(76561198000009702);
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Registry_RequiresSeparatePermissionsAndSelfHistoryRequiresCaller()
    {
        var permissions = new SelectivePermissions();
        var registry = new CommandRegistry(permissions);
        var warnings = new FakeWarnings();
        using var commands = new WarningCommandController(registry,
            new WarningCommandExecutor(new FakeTargets(), warnings, new FakeAudit(), new FakeNotifier(), new FixedTime(Now)));

        Assert.AreEqual(new PermissionId("ano.admin.warn"),
            registry.GetCommands().Single(x => x.Name == "anowarn").Permission);
        Assert.AreEqual(new PermissionId("ano.admin.warns"),
            registry.GetCommands().Single(x => x.Name == "anowarns").Permission);
        Assert.AreEqual(new PermissionId("ano.admin.clearwarns"),
            registry.GetCommands().Single(x => x.Name == "anoclearwarns").Permission);
        Assert.IsNull(registry.GetCommands().Single(x => x.Name == "anomywarns").Permission);
        Assert.AreEqual(CommandFailureReason.Forbidden,
            (await registry.ExecuteAsync("!anowarn Target 5 reason", Actor)).FailureReason);
        Assert.AreEqual(0, warnings.Writes);
        Assert.AreEqual(CommandFailureReason.InvalidInput,
            (await registry.ExecuteAsync("!anomywarns", null)).FailureReason);
        Assert.IsTrue((await registry.ExecuteAsync("!anomywarns", Actor)).Success);
        Assert.AreEqual(Actor, warnings.LastReadTarget);
        commands.Dispose();
        Assert.AreEqual(0, registry.GetCommands().Count);
    }

    [TestMethod]
    public async Task Warn_ValidatesBeforeResolutionAndRequiresOnlineTarget()
    {
        var targets = new FakeTargets { Result = ModerationTargetResult.Success(Target) };
        var warnings = new FakeWarnings();
        var audit = new FakeAudit();
        var notifier = new FakeNotifier();
        var executor = new WarningCommandExecutor(targets, warnings, audit, notifier, new FixedTime(Now));

        Assert.AreEqual(CommandFailureReason.InvalidInput,
            (await executor.WarnAsync(Actor, "Target", -1, "reason")).FailureReason);
        Assert.AreEqual(0, targets.Calls);
        Assert.AreEqual(CommandFailureReason.InvalidInput,
            (await executor.WarnAsync(Actor, "Target", 5, "reason")).FailureReason);
        Assert.AreEqual(0, warnings.Writes);
        targets.Result = ModerationTargetResult.Success(
            new PlayerSnapshot(Target, PlayerSessionId.New(), "Target", true, true, PlayerTeam.Terrorist, Now, Now));
        var result = await executor.WarnAsync(Actor, "Target", 5, "reason");
        Assert.IsTrue(result.Success);
        Assert.AreEqual(Now.AddMinutes(5), warnings.LastExpires);
        Assert.AreEqual(Target, notifier.Target);
        Assert.AreEqual(1, notifier.Calls);
        CollectionAssert.AreEqual(new[] { "warning.requested", "warning.completed" }, audit.Actions);
    }

    [TestMethod]
    public async Task Clear_PersistsAttemptAndReturnsNoSuccessForEmptyOrDeniedTarget()
    {
        var targets = new FakeTargets { Result = ModerationTargetResult.Reject(ModerationTargetFailure.TargetImmune) };
        var warnings = new FakeWarnings();
        var audit = new FakeAudit();
        var executor = new WarningCommandExecutor(targets, warnings, audit, new FixedTime(Now));
        Assert.AreEqual(CommandFailureReason.Forbidden,
            (await executor.ClearAsync(Actor, "Target", "resolved")).FailureReason);
        Assert.AreEqual(0, warnings.Writes);
        Assert.AreEqual(0, audit.Actions.Count);
    }

    [TestMethod]
    public async Task RequestedAuditFailure_PreventsWarningWrite()
    {
        var targets = new FakeTargets
        {
            Result = ModerationTargetResult.Success(
                new PlayerSnapshot(Target, PlayerSessionId.New(), "Target", true, true,
                    PlayerTeam.Terrorist, Now, Now)),
        };
        var warnings = new FakeWarnings();
        var audit = new FakeAudit { RejectRequest = true };
        var executor = new WarningCommandExecutor(targets, warnings, audit, new FixedTime(Now));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await executor.WarnAsync(Actor, "Target", 10, "reason"));
        Assert.AreEqual(0, warnings.Writes);
    }

    [TestMethod]
    public async Task ReconnectDuringAudit_DoesNotWriteWarningToReplacementSession()
    {
        var current = new PlayerSnapshot(Target, PlayerSessionId.New(), "Target", true, true,
            PlayerTeam.Terrorist, Now, Now);
        var replacement = new PlayerSnapshot(Target, PlayerSessionId.New(), "Target", true, true,
            PlayerTeam.Terrorist, Now, Now);
        var targets = new FakeTargets
        {
            Result = ModerationTargetResult.Success(current),
            SecondResult = ModerationTargetResult.Success(replacement),
        };
        var warnings = new FakeWarnings();
        var audit = new FakeAudit();
        var executor = new WarningCommandExecutor(targets, warnings, audit, new FixedTime(Now));

        var result = await executor.WarnAsync(Actor, "Target", 10, "reason");
        Assert.AreEqual(CommandFailureReason.InvalidInput, result.FailureReason);
        Assert.AreEqual(0, warnings.Writes);
        CollectionAssert.AreEqual(new[] { "warning.requested" }, audit.Actions);
    }

    private sealed class FakeNotifier : IWarningNotifier
    {
        public PlayerId? Target { get; private set; }
        public int Calls { get; private set; }
        public void Notify(PlayerId targetId, PlayerSessionId sessionId, string message)
        {
            Target = targetId;
            Calls++;
        }
    }

    private sealed class SelectivePermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class FakeTargets : IModerationTargetGateway
    {
        public ModerationTargetResult Result { get; set; } = ModerationTargetResult.Success(Target);
        public ModerationTargetResult? SecondResult { get; set; }
        public int Calls { get; private set; }
        public ValueTask<ModerationTargetResult> ResolveAsync(string selector, PlayerId? actor,
            PermissionId permission, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(Calls == 2 && SecondResult is not null ? SecondResult : Result);
        }
    }

    private sealed class FakeWarnings : IWarningService
    {
        public int Writes { get; private set; }
        public PlayerId? LastReadTarget { get; private set; }
        public DateTimeOffset? LastExpires { get; private set; }
        public ValueTask<WarningRecord> WarnAsync(PlayerId targetId, PlayerId? actorId, string reason,
            DateTimeOffset atUtc, DateTimeOffset? expiresAtUtc = null, CancellationToken cancellationToken = default)
        {
            Writes++;
            LastExpires = expiresAtUtc;
            return ValueTask.FromResult(new WarningRecord(Guid.NewGuid(), targetId, actorId, reason, atUtc, expiresAtUtc));
        }
        public ValueTask<IReadOnlyList<WarningRecord>> GetActiveAsync(PlayerId targetId, DateTimeOffset atUtc,
            int limit = 100, CancellationToken cancellationToken = default)
        {
            LastReadTarget = targetId;
            return ValueTask.FromResult<IReadOnlyList<WarningRecord>>([]);
        }
        public ValueTask<IReadOnlyList<WarningRecord>> GetHistoryAsync(PlayerId targetId, int limit = 100,
            CancellationToken cancellationToken = default)
        {
            LastReadTarget = targetId;
            return ValueTask.FromResult<IReadOnlyList<WarningRecord>>([]);
        }
        public ValueTask<IReadOnlyList<WarningRecord>> ClearAsync(PlayerId targetId, PlayerId? actorId,
            string reason, DateTimeOffset atUtc, CancellationToken cancellationToken = default)
        {
            Writes++;
            return ValueTask.FromResult<IReadOnlyList<WarningRecord>>([]);
        }
    }

    private sealed class FakeAudit : IAdminAuditService
    {
        public List<string> Actions { get; } = [];
        public bool RejectRequest { get; set; }
        public ValueTask<AdminAuditEntry> RecordAsync(AdminActionId action, PlayerId? actorId,
            PlayerId? targetId, string reason, DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (RejectRequest && action.Value.EndsWith(".requested", StringComparison.Ordinal))
                throw new InvalidOperationException("Audit storage unavailable.");
            Actions.Add(action.Value);
            return ValueTask.FromResult(new AdminAuditEntry(Guid.NewGuid(), action, actorId,
                targetId, reason, occurredAtUtc));
        }
        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetRecentAsync(int limit = 100,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetTargetHistoryAsync(PlayerId targetId,
            int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
