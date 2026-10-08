using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Commands;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ExtendedInventoryTeamCommandControllerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 17, 0, 0, TimeSpan.Zero);
    private static readonly PlayerId TargetId = new(76561198000016712);

    [TestMethod]
    public void RegistersExpectedCommandsAndPermissions()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        using var controller = CreateController(registry, out _, out _);

        var commands = registry.GetCommands().ToDictionary(x => x.Name);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "anorename",
                "anostrip",
                "anogive",
                "anoteam",
                "anoswap",
                "anohide",
            },
            commands.Keys.ToArray());

        Assert.AreEqual(new PermissionId("ano.admin.rename"), commands["anorename"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.strip"), commands["anostrip"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.give"), commands["anogive"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.team"), commands["anoteam"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.swap"), commands["anoswap"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.hide"), commands["anohide"].Permission);
        CollectionAssert.Contains(commands["anohide"].Aliases.ToArray(), "anostealth");
    }

    [TestMethod]
    public async Task QuotedRenameAndCatalogGiveRouteTypedActions()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        using var controller = CreateController(registry, out var transport, out _);

        var rename = await registry.ExecuteAsync(
            "anorename Target \"New Display Name\"",
            null);
        var give = await registry.ExecuteAsync(
            "anogive Target awp",
            null);

        Assert.IsTrue(rename.Success);
        Assert.IsTrue(give.Success);
        Assert.AreEqual("New Display Name", transport.Renamed.Single());
        Assert.AreEqual("weapon_awp", transport.Given.Single());
    }

    [TestMethod]
    public async Task RegistryPermissionDenialStopsBeforeTargetGateway()
    {
        var registry = new CommandRegistry(new DenyAllPermissions());
        using var controller = CreateController(registry, out _, out var targets);

        var result = await registry.ExecuteAsync(
            "anostrip Target",
            new PlayerId(76561198000016799));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.AreEqual(0, targets.Calls);
    }

    [TestMethod]
    public void CollisionRollsBackEarlierRegistrations()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        registry.Register(
            new ModuleId("collision"),
            new CommandDescriptor("anoteam", "occupied"),
            _ => ValueTask.FromResult(CommandResult.Ok()));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CreateController(registry, out _, out _));

        CollectionAssert.AreEqual(
            new[] { "anoteam" },
            registry.GetCommands().Select(x => x.Name).ToArray());
    }

    [TestMethod]
    public void DisposeRemovesEveryOwnedCommand()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        var controller = CreateController(registry, out _, out _);

        controller.Dispose();
        controller.Dispose();

        Assert.AreEqual(0, registry.GetCommands().Count);
    }

    [TestMethod]
    public async Task CssHandlerOwnsOnlyTeamCommandsAndIsDisposedOnce()
    {
        var registry = new CommandRegistry(new DenyAllPermissions());
        var teams = new FakeTeams();
        var controller = CreateController(registry, out _, out var targets, teams);
        var commands = registry.GetCommands().ToDictionary(value => value.Name);
        Assert.IsNull(commands["anoteam"].Permission);
        Assert.IsNull(commands["anoswap"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.rename"), commands["anorename"].Permission);
        Assert.IsFalse(commands.ContainsKey("css_switch"));
        var actor = new PlayerId(76561198000016799);
        var denied = await registry.ExecuteAsync("anoteam @me ct", actor);
        Assert.AreEqual(CommandFailureReason.Forbidden, denied.FailureReason);
        teams.HasFlag = true;
        Assert.IsTrue((await registry.ExecuteAsync("anoteam @me ct", actor)).Success);
        Assert.AreEqual((ExtendedInventoryTeamOperation.SetTeam, actor, "@me", "ct"), teams.Last);
        Assert.IsTrue((await registry.ExecuteAsync("anoswap #123", actor)).Success);
        Assert.AreEqual((ExtendedInventoryTeamOperation.SwapTeam, actor, "#123", (string?)null), teams.Last);
        Assert.IsFalse((await registry.ExecuteAsync("anostrip Target", actor)).Success);
        Assert.AreEqual(0, targets.Calls);
        controller.Dispose();
        controller.Dispose();
        Assert.AreEqual(1, teams.Disposals);
        Assert.AreEqual(0, registry.GetCommands().Count);
    }

    [TestMethod]
    public void CollisionDisposesOwnedCssHandler()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        registry.Register(new ModuleId("collision"), new CommandDescriptor("anoswap", "occupied"),
            _ => ValueTask.FromResult(CommandResult.Ok()));
        var teams = new FakeTeams();
        Assert.ThrowsExactly<InvalidOperationException>(() => CreateController(registry, out _, out _, teams));
        Assert.AreEqual(1, teams.Disposals);
        CollectionAssert.AreEqual(new[] { "anoswap" }, registry.GetCommands().Select(x => x.Name).ToArray());
    }

    private sealed class FakeTeams : ITeamAdministrationCommandHandler
    {
        public bool HasFlag { get; set; }
        public int Disposals { get; private set; }
        public (ExtendedInventoryTeamOperation, PlayerId?, string, string?) Last { get; private set; }
        public ValueTask<CommandResult> ExecuteAsync(ExtendedInventoryTeamOperation operation, PlayerId? actor,
            string selector, string? requestedTeam, CancellationToken cancellationToken = default)
        {
            Last = (operation, actor, selector, requestedTeam);
            return ValueTask.FromResult(CssTeamAdministrationPolicy.Authorize(HasFlag) ?? CommandResult.Ok());
        }
        public void Dispose() => Disposals++;
    }

    private static ExtendedInventoryTeamCommandController CreateController(
        IAnoCommandRegistry registry,
        out RecordingTransport transport,
        out FakeTargets targets,
        ITeamAdministrationCommandHandler? teams = null)
    {
        transport = new RecordingTransport();
        targets = new FakeTargets(
            ModerationTargetResult.Success(Player()));

        return new ExtendedInventoryTeamCommandController(
            registry,
            new ExtendedInventoryTeamCommandExecutor(
                targets,
                new EmptyPlayerRegistry(),
                new AllowAllPermissions(),
                transport),
            teams);
    }

    private static PlayerSnapshot Player() => new(
        TargetId,
        PlayerSessionId.New(),
        "Target",
        true,
        true,
        PlayerTeam.Terrorist,
        Now,
        Now);

    private sealed class FakeTargets(ModerationTargetResult result)
        : IModerationTargetGateway
    {
        public int Calls { get; private set; }

        public ValueTask<ModerationTargetResult> ResolveAsync(
            string selector,
            PlayerId? actor,
            PermissionId permission,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class EmptyPlayerRegistry : IPlayerRegistry
    {
        public IReadOnlyCollection<PlayerSnapshot> OnlinePlayers => [];

        public bool TryGet(PlayerId id, out PlayerSnapshot? player)
        {
            player = null;
            return false;
        }

        public ValueTask<PlayerSnapshot> ConnectAsync(
            PlayerConnection connection,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<PlayerSnapshot?> UpdateAsync(
            PlayerStateUpdate update,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<PlayerSnapshot?> DisconnectAsync(
            PlayerId id,
            PlayerSessionId sessionId,
            DateTimeOffset disconnectedAtUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingTransport : IExtendedInventoryTeamTransport
    {
        public List<string> Renamed { get; } = [];
        public List<string> Given { get; } = [];

        public ValueTask RenameAsync(
            PlayerSnapshot player,
            string name,
            CancellationToken cancellationToken = default)
        {
            Renamed.Add(name);
            return ValueTask.CompletedTask;
        }

        public ValueTask StripAsync(
            PlayerSnapshot player,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<bool> GiveAsync(
            PlayerSnapshot player,
            AdminItemDefinition item,
            CancellationToken cancellationToken = default)
        {
            Given.Add(item.ClassName);
            return ValueTask.FromResult(true);
        }

        public ValueTask SetTeamAsync(
            PlayerSnapshot player,
            PlayerTeam team,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask HideAsync(
            PlayerSnapshot player,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class AllowAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class DenyAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }
}
