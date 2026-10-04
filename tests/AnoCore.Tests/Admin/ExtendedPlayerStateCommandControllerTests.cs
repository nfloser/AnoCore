using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Commands;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ExtendedPlayerStateCommandControllerTests
{
    private static readonly PlayerId TargetId = new(76561198000016512);
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 15, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void RegistersExpectedCommandsAndPermissions()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        using var service = new ExtendedPlayerStateService(new RecordingTransport());
        using var controller = new ExtendedPlayerStateCommandController(
            registry,
            new ExtendedPlayerStateCommandExecutor(
                new FakeTargets(ModerationTargetResult.Success(Player())),
                service));

        var commands = registry.GetCommands().ToDictionary(x => x.Name);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "anohealth", "anoarmor", "anofreeze", "anounfreeze",
                "anonoclip", "anowalk", "anoslay", "anospeed",
                "anoresetspeed", "anoblind", "anounblind", "anogod", "anoungod",
            },
            commands.Keys.ToArray());

        Assert.AreEqual(new PermissionId("ano.admin.health"), commands["anohealth"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.freeze"), commands["anofreeze"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.noclip"), commands["anonoclip"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.speed"), commands["anospeed"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.blind"), commands["anoblind"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.god"), commands["anogod"].Permission);
        CollectionAssert.Contains(commands["anowalk"].Aliases.ToArray(), "anoclipoff");
        Assert.IsFalse(commands["anoblind"].Arguments[1].Required);
    }

    [TestMethod]
    public async Task OptionalBlindAlphaDefaultsAndExplicitValueAreRouted()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        var transport = new RecordingTransport();
        using var service = new ExtendedPlayerStateService(transport);
        using var controller = new ExtendedPlayerStateCommandController(
            registry,
            new ExtendedPlayerStateCommandExecutor(
                new FakeTargets(ModerationTargetResult.Success(Player())),
                service));

        var defaulted = await registry.ExecuteAsync("anoblind Target", null);
        var reset = await registry.ExecuteAsync("anounblind Target", null);
        var explicitValue = await registry.ExecuteAsync("anoblind Target 128", null);

        Assert.IsTrue(defaulted.Success);
        Assert.IsTrue(reset.Success);
        Assert.IsTrue(explicitValue.Success);
        Assert.AreEqual(255, transport.Applied[0].Value);
        Assert.AreEqual(128, transport.Applied[1].Value);
    }

    [TestMethod]
    public async Task PlayerPermissionGateRunsBeforeTargetResolution()
    {
        var registry = new CommandRegistry(new DenyAllPermissions());
        var targets = new FakeTargets(ModerationTargetResult.Success(Player()));
        using var service = new ExtendedPlayerStateService(new RecordingTransport());
        using var controller = new ExtendedPlayerStateCommandController(
            registry,
            new ExtendedPlayerStateCommandExecutor(targets, service));

        var result = await registry.ExecuteAsync(
            "anohealth Target 100",
            new PlayerId(76561198000016599));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.AreEqual(0, targets.Calls);
    }

    [TestMethod]
    public void RegistrationCollisionRollsBackEarlierCommands()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        registry.Register(
            new ModuleId("collision"),
            new CommandDescriptor("anoslay", "occupied"),
            _ => ValueTask.FromResult(CommandResult.Ok()));

        using var service = new ExtendedPlayerStateService(new RecordingTransport());
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new ExtendedPlayerStateCommandController(
                registry,
                new ExtendedPlayerStateCommandExecutor(
                    new FakeTargets(ModerationTargetResult.Success(Player())),
                    service)));

        CollectionAssert.AreEqual(
            new[] { "anoslay" },
            registry.GetCommands().Select(x => x.Name).ToArray());
    }

    [TestMethod]
    public void DisposeUnregistersAllOwnedCommands()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        using var service = new ExtendedPlayerStateService(new RecordingTransport());
        var controller = new ExtendedPlayerStateCommandController(
            registry,
            new ExtendedPlayerStateCommandExecutor(
                new FakeTargets(ModerationTargetResult.Success(Player())),
                service));

        controller.Dispose();
        controller.Dispose();

        Assert.AreEqual(0, registry.GetCommands().Count);
    }

    private static PlayerSnapshot Player() => new(
        TargetId,
        PlayerSessionId.New(),
        "Target",
        true,
        true,
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

    private sealed class RecordingTransport : IExtendedPlayerStateTransport
    {
        public List<ExtendedPlayerStateMutation> Applied { get; } = [];

        public ValueTask<ExtendedPlayerStateBaseline> CaptureAsync(
            PlayerSnapshot player,
            ExtendedPlayerStateFacet facet,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ExtendedPlayerStateBaseline(facet));

        public ValueTask ApplyAsync(
            PlayerSnapshot player,
            ExtendedPlayerStateMutation mutation,
            CancellationToken cancellationToken = default)
        {
            Applied.Add(mutation);
            return ValueTask.CompletedTask;
        }

        public ValueTask RestoreAsync(
            PlayerSnapshot player,
            ExtendedPlayerStateBaseline baseline,
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
