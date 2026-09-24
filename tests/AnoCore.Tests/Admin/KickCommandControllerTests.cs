using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Commands;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class KickCommandControllerTests
{
    private static readonly PlayerId Actor = new(76561198000009002);

    [TestMethod]
    public async Task Commands_RequireSeparatePermissionsAndDisposeUnregistersThem()
    {
        var permissions = new DenyPermissions();
        var registry = new CommandRegistry(permissions);
        var gateway = new CountingGateway();
        using var controller = new KickCommandController(
            registry,
            new KickCommandExecutor(
                gateway, new NoAudit(), new NoDisconnect(), new NoAnnouncement()));

        var descriptors = registry.GetCommands().ToDictionary(item => item.Name);
        Assert.AreEqual(new PermissionId("ano.admin.kick"), descriptors["anokick"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.silentkick"), descriptors["anosilentkick"].Permission);
        Assert.AreEqual(2, descriptors.Count);
        Assert.IsFalse(descriptors["anokick"].Arguments[1].Required);

        Assert.AreEqual(
            CommandFailureReason.Forbidden,
            (await registry.ExecuteAsync("!anokick Target reason", Actor)).FailureReason);
        Assert.AreEqual(
            CommandFailureReason.Forbidden,
            (await registry.ExecuteAsync("!anosilentkick Target reason", Actor)).FailureReason);
        Assert.AreEqual(0, gateway.Calls);

        controller.Dispose();
        Assert.AreEqual(0, registry.GetCommands().Count);
    }

    private sealed class DenyPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId, PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class CountingGateway : IModerationTargetGateway
    {
        public int Calls { get; private set; }

        public ValueTask<ModerationTargetResult> ResolveAsync(
            string selector, PlayerId? actor, PermissionId permission,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(ModerationTargetResult.Reject(ModerationTargetFailure.NotFound));
        }
    }

    private sealed class NoAudit : IAdminAuditService
    {
        public ValueTask<AdminAuditEntry> RecordAsync(
            AdminActionId action, PlayerId? actorId, PlayerId? targetId, string reason,
            DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetRecentAsync(
            int limit = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetTargetHistoryAsync(
            PlayerId targetId, int limit = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoDisconnect : IPlayerDisconnectAction
    {
        public ValueTask DisconnectAsync(
            PlayerSnapshot player, string reason,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoAnnouncement : IKickAnnouncement
    {
        public ValueTask AnnounceAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
