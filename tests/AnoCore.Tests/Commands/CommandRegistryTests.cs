using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Commands;

[TestClass]
public sealed class CommandRegistryTests
{
    private static readonly PlayerId Player = new(76561198000000991);
    private static readonly ModuleId Owner = new("tests");

    [TestMethod]
    public async Task ExecuteAsync_ResolvesAliasAndQuotedArguments()
    {
        var calls = new List<CommandContext>();
        var registry = new CommandRegistry(new AllowAllPermissions());
        registry.Register(Owner, new CommandDescriptor("anotest", "test", aliases: ["anot"]), context =>
        {
            calls.Add(context);
            return ValueTask.FromResult(CommandResult.Ok("done"));
        });

        var result = await registry.ExecuteAsync("!anot \"hello world\" 42", Player);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("done", result.Message);
        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "hello world", "42" }, calls[0].Arguments.ToArray());
    }

    [TestMethod]
    public async Task ExecuteAsync_RejectsUnauthorizedCallerBeforeHandler()
    {
        var invoked = false;
        var registry = new CommandRegistry(new DenyAllPermissions());
        registry.Register(
            Owner,
            new CommandDescriptor("anoadmin", "admin", new PermissionId("ano.admin.use")),
            _ =>
            {
                invoked = true;
                return ValueTask.FromResult(CommandResult.Ok());
            });

        var result = await registry.ExecuteAsync("!anoadmin", Player);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.IsFalse(invoked);
    }

    [TestMethod]
    public void Register_RejectsDuplicateCommandOrAlias()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        registry.Register(Owner, new CommandDescriptor("anoone", "one", aliases: ["anox"]), _ => ValueTask.FromResult(CommandResult.Ok()));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            registry.Register(new ModuleId("other"), new CommandDescriptor("anox", "collision"), _ => ValueTask.FromResult(CommandResult.Ok())));
    }

    [TestMethod]
    public async Task UnregisterAll_RemovesOwnedCommandsAndAliases()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        registry.Register(Owner, new CommandDescriptor("anoone", "one", aliases: ["anox"]), _ => ValueTask.FromResult(CommandResult.Ok()));

        registry.UnregisterAll(Owner);

        var direct = await registry.ExecuteAsync("!anoone", Player);
        var alias = await registry.ExecuteAsync("!anox", Player);
        Assert.AreEqual(CommandFailureReason.NotFound, direct.FailureReason);
        Assert.AreEqual(CommandFailureReason.NotFound, alias.FailureReason);
    }

    [TestMethod]
    public void Register_RejectsCommandsAndAliasesOutsideAnoNamespace()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());

        Assert.ThrowsExactly<ArgumentException>(() =>
            registry.Register(Owner, new CommandDescriptor("kick", "bad"), _ => ValueTask.FromResult(CommandResult.Ok())));
        Assert.ThrowsExactly<ArgumentException>(() =>
            registry.Register(Owner, new CommandDescriptor("anokick", "bad alias", aliases: ["k"]), _ => ValueTask.FromResult(CommandResult.Ok())));
    }

    private sealed class AllowAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class DenyAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }
}
