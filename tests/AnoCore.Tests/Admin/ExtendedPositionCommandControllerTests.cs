using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Targeting;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Commands;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ExtendedPositionCommandControllerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 16, 0, 0, TimeSpan.Zero);
    private static readonly PlayerId TargetId = new(76561198000016612);
    private static readonly PlayerId DestinationId = new(76561198000016613);

    [TestMethod]
    public void RegistersExpectedCommandsAndExplicitPermissions()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        using var controller = CreateController(registry, out _, out _);

        var commands = registry.GetCommands().ToDictionary(x => x.Name);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "anorespawn",
                "anorevive",
                "anotppos",
                "anotp",
                "anobury",
                "anounbury",
                "anoslap",
            },
            commands.Keys.ToArray());

        Assert.AreEqual(
            new PermissionId("ano.admin.respawn"),
            commands["anorespawn"].Permission);
        Assert.AreEqual(
            new PermissionId("ano.admin.revive"),
            commands["anorevive"].Permission);
        Assert.AreEqual(
            new PermissionId("ano.admin.teleport"),
            commands["anotp"].Permission);
        Assert.AreEqual(
            new PermissionId("ano.admin.bury"),
            commands["anobury"].Permission);
        Assert.AreEqual(
            new PermissionId("ano.admin.slap"),
            commands["anoslap"].Permission);
        CollectionAssert.Contains(commands["anotp"].Aliases.ToArray(), "anoteleport");
        CollectionAssert.Contains(commands["anotppos"].Aliases.ToArray(), "anoteleportpos");
        Assert.IsFalse(commands["anoslap"].Arguments[1].Required);
    }

    [TestMethod]
    public async Task TeleportPositionAcceptsDecimalInvariantCoordinates()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        using var controller = CreateController(registry, out var transport, out _);

        var result = await registry.ExecuteAsync(
            "anotppos Target 10.5 -20.25 30",
            null);

        Assert.IsTrue(result.Success);
        var teleport = transport.Teleports.Single();
        Assert.AreEqual(10.5f, teleport.Position.X);
        Assert.AreEqual(-20.25f, teleport.Position.Y);
        Assert.AreEqual(30f, teleport.Position.Z);
    }

    [TestMethod]
    public async Task SlapOptionalDamageDefaultsToZero()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        using var controller = CreateController(registry, out var transport, out _);

        var result = await registry.ExecuteAsync("anoslap Target", null);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, transport.Slaps.Single());
    }

    [TestMethod]
    public async Task PermissionDenialStopsBeforeTargetGateway()
    {
        var registry = new CommandRegistry(new DenyAllPermissions());
        using var controller = CreateController(registry, out _, out var targets);

        var result = await registry.ExecuteAsync(
            "anobury Target",
            new PlayerId(76561198000016699));

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
            new CommandDescriptor("anotp", "occupied"),
            _ => ValueTask.FromResult(CommandResult.Ok()));

        var transport = new RecordingTransport();
        var service = new ExtendedPositionService(transport);
        var targets = new FakeTargets(
            ModerationTargetResult.Success(Player(TargetId, true)));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new ExtendedPositionCommandController(
                registry,
                new ExtendedPositionCommandExecutor(
                    targets,
                    new FakeResolver(Player(DestinationId, true)),
                    service)));

        CollectionAssert.AreEqual(
            new[] { "anotp" },
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

    private static ExtendedPositionCommandController CreateController(
        IAnoCommandRegistry registry,
        out RecordingTransport transport,
        out FakeTargets targets)
    {
        transport = new RecordingTransport();
        targets = new FakeTargets(
            ModerationTargetResult.Success(Player(TargetId, true)));
        return new ExtendedPositionCommandController(
            registry,
            new ExtendedPositionCommandExecutor(
                targets,
                new FakeResolver(Player(DestinationId, true)),
                new ExtendedPositionService(transport)));
    }

    private static PlayerSnapshot Player(PlayerId id, bool alive) => new(
        id,
        PlayerSessionId.New(),
        id == DestinationId ? "Destination" : "Target",
        true,
        alive,
        PlayerTeam.CounterTerrorist,
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

    private sealed class FakeResolver(PlayerSnapshot destination)
        : IPlayerTargetResolver
    {
        public TargetResolutionResult Resolve(
            string selector,
            PlayerId? caller = null,
            TargetSelectorCapabilities capabilities = TargetSelectorCapabilities.None)
            => string.Equals(
                    selector,
                    destination.Name,
                    StringComparison.OrdinalIgnoreCase)
                ? TargetResolutionResult.Success([destination])
                : TargetResolutionResult.Reject(TargetResolutionFailure.NotFound);
    }

    private sealed class RecordingTransport : IExtendedPositionTransport
    {
        public List<(PlayerSnapshot Player, PlayerWorldPosition Position)> Teleports { get; } = [];
        public List<int> Slaps { get; } = [];

        public ValueTask<PlayerWorldPosition> ReadPositionAsync(
            PlayerSnapshot player,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new PlayerWorldPosition(0, 0, 0));

        public ValueTask RespawnAsync(
            PlayerSnapshot player,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask TeleportAsync(
            PlayerSnapshot player,
            PlayerWorldPosition position,
            CancellationToken cancellationToken = default)
        {
            Teleports.Add((player, position));
            return ValueTask.CompletedTask;
        }

        public ValueTask SlapAsync(
            PlayerSnapshot player,
            int damage,
            CancellationToken cancellationToken = default)
        {
            Slaps.Add(damage);
            return ValueTask.CompletedTask;
        }
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
