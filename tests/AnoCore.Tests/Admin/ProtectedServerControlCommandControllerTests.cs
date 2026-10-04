using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Commands;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ProtectedServerControlCommandControllerTests
{
    [TestMethod]
    public void RegistersOnlyProtectedCommandsWithDedicatedPermissions()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        using var controller = CreateController(registry, out _);

        var commands = registry.GetCommands().ToDictionary(command => command.Name);

        CollectionAssert.AreEquivalent(
            new[] { "anocvar", "anoserver", "anosameip" },
            commands.Keys.ToArray());
        Assert.AreEqual(
            new PermissionId("ano.admin.cvar"),
            commands["anocvar"].Permission);
        Assert.AreEqual(
            new PermissionId("ano.admin.server"),
            commands["anoserver"].Permission);
        Assert.AreEqual(
            new PermissionId("ano.admin.sameip"),
            commands["anosameip"].Permission);
        CollectionAssert.Contains(
            commands["anosameip"].Aliases.ToArray(),
            "anoantighosting");
    }

    [TestMethod]
    public async Task ServerConsoleCommandsRouteThroughProtectedExecutor()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        using var controller = CreateController(registry, out var transport);

        var cvar = await registry.ExecuteAsync(
            "anocvar mp_friendlyfire 1",
            null);
        var server = await registry.ExecuteAsync(
            "anoserver mp_restartgame 1",
            null);

        Assert.IsTrue(cvar.Success);
        Assert.IsTrue(server.Success);
        CollectionAssert.AreEqual(
            new[] { ("mp_friendlyfire", "1") },
            transport.CVarWrites);
        CollectionAssert.AreEqual(
            new[] { ("mp_restartgame", "1") },
            transport.ServerCommands);
    }

    [TestMethod]
    public void RegistrationCollisionRollsBackEarlierCommands()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        registry.Register(
            new ModuleId("collision"),
            new CommandDescriptor("anoserver", "occupied"),
            _ => ValueTask.FromResult(CommandResult.Ok()));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CreateController(registry, out _));

        CollectionAssert.AreEqual(
            new[] { "anoserver" },
            registry.GetCommands().Select(command => command.Name).ToArray());
    }

    [TestMethod]
    public void DisposeRemovesEveryOwnedCommand()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        var controller = CreateController(registry, out _);

        controller.Dispose();
        controller.Dispose();

        Assert.AreEqual(0, registry.GetCommands().Count);
    }

    private static ProtectedServerControlCommandController CreateController(
        IAnoCommandRegistry registry,
        out RecordingTransport transport)
    {
        transport = new RecordingTransport();
        var policy = ProtectedServerControlPolicy.Create(
            new ProtectedServerControlConfiguration
            {
                AllowedConVars = ["mp_friendlyfire"],
                AllowedServerCommands = ["mp_restartgame"],
            });

        return new ProtectedServerControlCommandController(
            registry,
            new ProtectedServerControlExecutor(
                policy,
                new AllowAllPermissions(),
                new RecordingAudit(),
                transport));
    }

    private sealed class RecordingTransport : IProtectedServerControlTransport
    {
        public List<(string Name, string Value)> CVarWrites { get; } = [];
        public List<(string Command, string Arguments)> ServerCommands { get; } = [];

        public ValueTask<bool> SetCVarAsync(
            string name,
            string value,
            CancellationToken cancellationToken = default)
        {
            CVarWrites.Add((name, value));
            return ValueTask.FromResult(true);
        }

        public ValueTask ExecuteServerCommandAsync(
            string command,
            string arguments,
            CancellationToken cancellationToken = default)
        {
            ServerCommands.Add((command, arguments));
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<SameIpPlayerGroup>> GetSameIpGroupsAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<SameIpPlayerGroup>>([]);
    }

    private sealed class RecordingAudit : IAdminAuditService
    {
        public ValueTask<AdminAuditEntry> RecordAsync(
            AdminActionId action,
            PlayerId? actorId,
            PlayerId? targetId,
            string reason,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(
                new AdminAuditEntry(
                    Guid.NewGuid(),
                    action,
                    actorId,
                    targetId,
                    reason,
                    occurredAtUtc));

        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetRecentAsync(
            int limit = 100,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetTargetHistoryAsync(
            PlayerId targetId,
            int limit = 100,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class AllowAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }
}
