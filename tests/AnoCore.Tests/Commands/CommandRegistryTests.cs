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
    public async Task ExecuteAsync_ParsesTypedArgumentsBeforeHandler()
    {
        CommandContext? received = null;
        var registry = new CommandRegistry(new AllowAllPermissions());
        var descriptor = new CommandDescriptor(
            "anotyped",
            "typed",
            arguments:
            [
                new CommandArgumentDescriptor("rounds", CommandArgumentKind.Int32, "Round count"),
                new CommandArgumentDescriptor("enabled", CommandArgumentKind.Boolean, "Enable flag"),
            ]);
        registry.Register(Owner, descriptor, context =>
        {
            received = context;
            return ValueTask.FromResult(CommandResult.Ok());
        });

        var result = await registry.ExecuteAsync("!anotyped 12 yes", Player);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(received);
        Assert.AreEqual(12, received.Get<int>("rounds"));
        Assert.IsTrue(received.Get<bool>("enabled"));
    }

    [TestMethod]
    public async Task ExecuteAsync_RejectsInvalidTypedArgumentsBeforeHandler()
    {
        var invoked = false;
        var registry = new CommandRegistry(new AllowAllPermissions());
        var descriptor = new CommandDescriptor(
            "anotyped",
            "typed",
            arguments: [new CommandArgumentDescriptor("rounds", CommandArgumentKind.Int32, "Round count")]);
        registry.Register(Owner, descriptor, _ =>
        {
            invoked = true;
            return ValueTask.FromResult(CommandResult.Ok());
        });

        var result = await registry.ExecuteAsync("!anotyped nope", Player);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, result.FailureReason);
        Assert.IsFalse(invoked);
        StringAssert.Contains(result.Message, "rounds");
    }

    [TestMethod]
    public async Task ExecuteAsync_RejectsMissingRequiredAndUnexpectedArguments()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        var descriptor = new CommandDescriptor(
            "anotyped",
            "typed",
            arguments:
            [
                new CommandArgumentDescriptor("required", CommandArgumentKind.String, "Required"),
                new CommandArgumentDescriptor("optional", CommandArgumentKind.String, "Optional", required: false),
            ]);
        registry.Register(Owner, descriptor, _ => ValueTask.FromResult(CommandResult.Ok()));

        var missing = await registry.ExecuteAsync("!anotyped", Player);
        var tooMany = await registry.ExecuteAsync("!anotyped one two three", Player);

        Assert.AreEqual(CommandFailureReason.InvalidInput, missing.FailureReason);
        Assert.AreEqual(CommandFailureReason.InvalidInput, tooMany.FailureReason);
    }

    [TestMethod]
    public void GetCommands_ExposesHelpMetadataAndGeneratedUsage()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        registry.Register(
            Owner,
            new CommandDescriptor(
                "anohelped",
                "Does a thing",
                arguments:
                [
                    new CommandArgumentDescriptor("target", CommandArgumentKind.String, "Target player"),
                    new CommandArgumentDescriptor("reason", CommandArgumentKind.String, "Optional reason", required: false),
                ]),
            _ => ValueTask.FromResult(CommandResult.Ok()));

        var command = registry.GetCommands().Single();

        Assert.AreEqual("anohelped <target> [reason]", command.Usage);
        Assert.AreEqual("Does a thing", command.Description);
        Assert.AreEqual("Target player", command.Arguments[0].Description);
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
