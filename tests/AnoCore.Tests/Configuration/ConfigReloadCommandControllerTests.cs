using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Configuration;

namespace AnoCore.Tests.Configuration;

[TestClass]
public sealed class ConfigReloadCommandControllerTests
{
    private static readonly PlayerId Operator = new(76561198000000801);
    private static readonly PlayerId Player = new(76561198000000802);

    [TestMethod]
    public async Task Commands_ListStableDescriptorsAndReloadSelectedConfiguration()
    {
        var registry = new ConfigReloadRegistry();
        var commands = new CommandRegistry(new OperatorPermissions());
        var firstValue = 1;
        using var first = registry.Register(
            new ModuleId("zeta"), "zeta.config", 0,
            _ => ValueTask.FromResult(firstValue));
        using var second = registry.Register(
            new ModuleId("alpha"), "alpha.config", 0,
            _ => ValueTask.FromResult(2));
        using var controller = new ConfigReloadCommandController(commands, registry);

        var listed = await commands.ExecuteAsync("anoconfigs", Operator);
        var reloaded = await commands.ExecuteAsync("anoreloadconfig zeta.config", Operator);

        Assert.IsTrue(listed.Success, listed.Message);
        Assert.AreEqual(
            "[ANO] Reloadable configurations: alpha.config (alpha) | zeta.config (zeta).",
            listed.Message);
        Assert.IsTrue(reloaded.Success, reloaded.Message);
        Assert.AreEqual("[ANO] Configuration 'zeta.config' reloaded.", reloaded.Message);
        Assert.AreEqual(1, first.Current);
        Assert.AreEqual(0, second.Current);
    }

    [TestMethod]
    public async Task Commands_RequirePermissionForPlayersButAllowConsole()
    {
        var registry = new ConfigReloadRegistry();
        var commands = new CommandRegistry(new OperatorPermissions());
        using var registration = registry.Register(
            new ModuleId("tests"), "tests.config", 0,
            _ => ValueTask.FromResult(1));
        using var controller = new ConfigReloadCommandController(commands, registry);

        Assert.AreEqual(
            CommandFailureReason.Forbidden,
            (await commands.ExecuteAsync("anoconfigs", Player)).FailureReason);
        Assert.AreEqual(
            CommandFailureReason.Forbidden,
            (await commands.ExecuteAsync("anoreloadconfig tests.config", Player)).FailureReason);
        Assert.IsTrue((await commands.ExecuteAsync("anoconfigs", null)).Success);
        Assert.IsTrue((await commands.ExecuteAsync("anoreloadconfig tests.config", null)).Success);
    }

    [TestMethod]
    public async Task ReloadCommand_RejectsMissingUnknownAndInvalidConfigurationWithoutPublishing()
    {
        var registry = new ConfigReloadRegistry();
        var commands = new CommandRegistry(new OperatorPermissions());
        var next = 0;
        using var registration = registry.Register(
            new ModuleId("tests"), "tests.config", 1,
            _ => ValueTask.FromResult(next),
            value => value > 0 ? [] : ["Must be positive."]);
        using var controller = new ConfigReloadCommandController(commands, registry);

        var missing = await commands.ExecuteAsync("anoreloadconfig", Operator);
        var unknown = await commands.ExecuteAsync("anoreloadconfig unknown.config", Operator);
        var invalid = await commands.ExecuteAsync("anoreloadconfig tests.config", Operator);

        Assert.AreEqual(CommandFailureReason.InvalidInput, missing.FailureReason);
        Assert.AreEqual(CommandFailureReason.HandlerFailed, unknown.FailureReason);
        Assert.AreEqual(CommandFailureReason.HandlerFailed, invalid.FailureReason);
        Assert.AreEqual(1, registration.Current);
    }

    [TestMethod]
    public async Task Dispose_RemovesBothCommands()
    {
        var commands = new CommandRegistry(new OperatorPermissions());
        var controller = new ConfigReloadCommandController(commands, new ConfigReloadRegistry());

        controller.Dispose();
        controller.Dispose();

        Assert.AreEqual(
            CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("anoconfigs", Operator)).FailureReason);
        Assert.AreEqual(
            CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("anoreloadconfig tests.config", Operator)).FailureReason);
    }

    private sealed class OperatorPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(playerId == Operator && permission.Value == "ano.core.reload");
    }
}
