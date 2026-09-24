using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class KickCommandExecutorTests
{
    private static readonly PlayerId TargetId = new(76561198000009001);
    private static readonly PlayerId ActorId = new(76561198000009002);
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 17, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Kick_UsesAuthorizedOnlineSessionAndAuditsBeforeAndAfterNativeAction()
    {
        var trace = new List<string>();
        var audit = new MemoryAudit(trace);
        var native = new FakeDisconnect(trace);
        var announcement = new FakeAnnouncement(trace);
        var executor = new KickCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(Player())),
            audit, native, announcement, new FixedTime(Now));

        var result = await executor.ExecuteAsync(false, ActorId, "Target", "  griefing  ");

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(
            new[] { "kick.requested", "disconnect", "kick", "announce" },
            trace);
        Assert.AreEqual(TargetId, audit.Entries[1].TargetId);
        Assert.AreEqual(ActorId, audit.Entries[1].ActorId);
        Assert.AreEqual("griefing", audit.Entries[1].Reason);
    }

    [TestMethod]
    public async Task SilentKick_IsAuditedWithoutPublicAnnouncement()
    {
        var trace = new List<string>();
        var executor = new KickCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(Player())),
            new MemoryAudit(trace), new FakeDisconnect(trace),
            new FakeAnnouncement(trace), new FixedTime(Now));

        var result = await executor.ExecuteAsync(true, ActorId, "Target", null);

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(
            new[] { "kick.silent.requested", "disconnect", "kick.silent" },
            trace);
    }

    [TestMethod]
    public async Task OfflineTargetAndNativeFailure_NeverReportSuccessfulKick()
    {
        var trace = new List<string>();
        var audit = new MemoryAudit(trace);
        var offline = new KickCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(TargetId)),
            audit, new FakeDisconnect(trace), new FakeAnnouncement(trace), new FixedTime(Now));

        var rejected = await offline.ExecuteAsync(false, ActorId, TargetId.ToString(), "reason");
        Assert.IsFalse(rejected.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, rejected.FailureReason);
        Assert.AreEqual(0, trace.Count);

        var native = new FakeDisconnect(trace) { Fail = true };
        var online = new KickCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(Player())),
            audit, native, new FakeAnnouncement(trace), new FixedTime(Now));
        var failed = await online.ExecuteAsync(false, ActorId, "Target", "reason");
        Assert.IsFalse(failed.Success);
        CollectionAssert.AreEqual(new[] { "kick.requested", "disconnect" }, trace);
    }

    [TestMethod]
    public async Task AuditFailure_PreventsNativeDisconnect()
    {
        var trace = new List<string>();
        var audit = new MemoryAudit(trace) { FailNext = true };
        var executor = new KickCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(Player())),
            audit, new FakeDisconnect(trace), new FakeAnnouncement(trace), new FixedTime(Now));

        var result = await executor.ExecuteAsync(false, ActorId, "Target", "reason");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, trace.Count);
    }

    [TestMethod]
    public async Task ImmuneTarget_IsRejectedBeforeAuditOrDisconnect()
    {
        var trace = new List<string>();
        var executor = new KickCommandExecutor(
            new FakeTargets(ModerationTargetResult.Reject(ModerationTargetFailure.TargetImmune)),
            new MemoryAudit(trace), new FakeDisconnect(trace),
            new FakeAnnouncement(trace), new FixedTime(Now));

        var result = await executor.ExecuteAsync(false, ActorId, "Target", "reason");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.AreEqual(0, trace.Count);
    }

    [TestMethod]
    public async Task CompletionAuditFailure_ReportsPartialKickAndRetainsRequestedEntry()
    {
        var trace = new List<string>();
        var audit = new MemoryAudit(trace) { FailAction = "kick" };
        var executor = new KickCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(Player())),
            audit, new FakeDisconnect(trace),
            new FakeAnnouncement(trace), new FixedTime(Now));

        var result = await executor.ExecuteAsync(false, ActorId, "Target", "reason");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.HandlerFailed, result.FailureReason);
        CollectionAssert.AreEqual(new[] { "kick.requested", "disconnect" }, trace);
    }

    private static PlayerSnapshot Player() => new(
        TargetId, PlayerSessionId.New(), "Target", true, true,
        PlayerTeam.CounterTerrorist, Now, Now);

    private sealed class FakeTargets(ModerationTargetResult result) : IModerationTargetGateway
    {
        public ValueTask<ModerationTargetResult> ResolveAsync(
            string selector, PlayerId? actor, PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(result);
    }

    private sealed class MemoryAudit(List<string> trace) : IAdminAuditService
    {
        public List<AdminAuditEntry> Entries { get; } = [];
        public bool FailNext { get; set; }
        public string? FailAction { get; set; }

        public ValueTask<AdminAuditEntry> RecordAsync(
            AdminActionId action, PlayerId? actorId, PlayerId? targetId, string reason,
            DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default)
        {
            if (FailNext || action.Value == FailAction)
            {
                FailNext = false;
                throw new InvalidOperationException("Database unavailable.");
            }

            var entry = new AdminAuditEntry(Guid.NewGuid(), action, actorId, targetId, reason, occurredAtUtc);
            Entries.Add(entry);
            trace.Add(action.Value);
            return ValueTask.FromResult(entry);
        }

        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetRecentAsync(
            int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetTargetHistoryAsync(
            PlayerId targetId, int limit = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeDisconnect(List<string> trace) : IPlayerDisconnectAction
    {
        public bool Fail { get; set; }
        public ValueTask DisconnectAsync(
            PlayerSnapshot player, string reason, CancellationToken cancellationToken = default)
        {
            trace.Add("disconnect");
            if (Fail) throw new InvalidOperationException("Native failure.");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAnnouncement(List<string> trace) : IKickAnnouncement
    {
        public ValueTask AnnounceAsync(CancellationToken cancellationToken = default)
        {
            trace.Add("announce");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
