using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Commands;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class RankAdjustmentCommandControllerTests
{
    private static readonly PlayerId Actor = new(76561198000013101);
    private static readonly PlayerId Target = new(76561198000013102);
    private static readonly DateTimeOffset Now =
        new(2026, 9, 26, 22, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Constructor_RegistersCommandsWithSeparatePermissions()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        using var controller = Create(registry, out _, out _);
        var commands = registry.GetCommands().ToDictionary(x => x.Name);

        CollectionAssert.AreEquivalent(new[] {
            "anogiverankpoints", "anotakerankpoints",
            "anosetrankpoints", "anoresetrankpoints" }, commands.Keys.ToArray());
        Assert.AreEqual(new PermissionId("ano.ranks.points.give"),
            commands["anogiverankpoints"].Permission);
        Assert.AreEqual(new PermissionId("ano.ranks.points.take"),
            commands["anotakerankpoints"].Permission);
        Assert.AreEqual(new PermissionId("ano.ranks.points.set"),
            commands["anosetrankpoints"].Permission);
        Assert.AreEqual(new PermissionId("ano.ranks.points.reset"),
            commands["anoresetrankpoints"].Permission);
        Assert.AreEqual(3, commands["anogiverankpoints"].Arguments.Count);
        Assert.AreEqual(2, commands["anoresetrankpoints"].Arguments.Count);
    }

    [TestMethod]
    public async Task Commands_RouteValuesReasonsActorAndPermission()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        using var controller = Create(registry, out var targets, out var administration);

        var give = await registry.ExecuteAsync(
            "!anogiverankpoints Target 25 \"manual reward\"", Actor);
        Assert.IsTrue(give.Success);
        Assert.AreEqual(RankAdjustmentAdminOperation.Give, administration.Operation);
        Assert.AreEqual(25L, administration.Points);
        Assert.AreEqual("manual reward", administration.Reason);
        Assert.AreEqual(Actor, administration.Actor);
        Assert.AreEqual(Target, administration.Target);
        Assert.AreEqual(new PermissionId("ano.ranks.points.give"), targets.Permission);

        var reset = await registry.ExecuteAsync("!anoresetrankpoints Target", Actor);
        Assert.IsTrue(reset.Success);
        Assert.AreEqual(RankAdjustmentAdminOperation.Reset, administration.Operation);
        Assert.AreEqual(0L, administration.Points);
        Assert.AreEqual(RankAdjustmentCommandExecutor.DefaultReason,
            administration.Reason);
    }

    [TestMethod]
    public async Task PermissionDenial_PrecedesTargetResolutionAndMutation()
    {
        var registry = new CommandRegistry(new DenyAllPermissions());
        using var controller = Create(registry, out var targets, out var administration);

        var result = await registry.ExecuteAsync(
            "!anosetrankpoints Target 10 reason", Actor);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.AreEqual(0, targets.Calls);
        Assert.AreEqual(0, administration.Calls);
    }

    [TestMethod]
    public async Task ImmuneTarget_IsRejectedWithoutMutation()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        var targets = new StubTargetGateway(Target)
        {
            Result = ModerationTargetResult.Reject(ModerationTargetFailure.TargetImmune)
        };
        var administration = new StubAdministrationService();
        using var controller = new RankAdjustmentCommandController(registry,
            new RankAdjustmentCommandExecutor(targets, administration,
                new FixedTimeProvider(Now)));

        var result = await registry.ExecuteAsync(
            "!anotakerankpoints Target 5 reason", Actor);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.AreEqual(0, administration.Calls);
    }

    [TestMethod]
    public void Constructor_RollsBackWhenLaterRegistrationCollides()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        registry.Register(new ModuleId("collision"),
            new CommandDescriptor("anosetrankpoints", "existing"),
            _ => ValueTask.FromResult(CommandResult.Ok()));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            Create(registry, out _, out _));
        CollectionAssert.AreEqual(new[] { "anosetrankpoints" },
            registry.GetCommands().Select(x => x.Name).ToArray());
    }

    [TestMethod]
    public void Dispose_UnregistersAllRankAdministrationCommands()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        var controller = Create(registry, out _, out _);
        controller.Dispose();
        Assert.AreEqual(0, registry.GetCommands().Count);
    }

    private static RankAdjustmentCommandController Create(
        IAnoCommandRegistry registry,
        out StubTargetGateway targets,
        out StubAdministrationService administration)
    {
        targets = new StubTargetGateway(Target);
        administration = new StubAdministrationService();
        return new RankAdjustmentCommandController(registry,
            new RankAdjustmentCommandExecutor(targets, administration,
                new FixedTimeProvider(Now)));
    }

    private sealed class StubTargetGateway(PlayerId target) : IModerationTargetGateway
    {
        public int Calls { get; private set; }
        public PermissionId? Permission { get; private set; }
        public ModerationTargetResult? Result { get; init; }

        public ValueTask<ModerationTargetResult> ResolveAsync(
            string selector, PlayerId? actor, PermissionId permission,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Permission = permission;
            return ValueTask.FromResult(Result
                ?? ModerationTargetResult.Success(target));
        }
    }

    private sealed class StubAdministrationService
        : IRankAdjustmentAdministrationService
    {
        public int Calls { get; private set; }
        public RankAdjustmentAdminOperation Operation { get; private set; }
        public PlayerId? Target { get; private set; }
        public PlayerId? Actor { get; private set; }
        public long Points { get; private set; }
        public string? Reason { get; private set; }

        public ValueTask<RankAdjustmentAdminResult> ApplyAsync(
            RankAdjustmentAdminOperation operation, PlayerId targetId,
            long points, PlayerId? actorId, string reason,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Operation = operation;
            Target = targetId;
            Actor = actorId;
            Points = points;
            Reason = reason;
            return ValueTask.FromResult(
                new RankAdjustmentAdminResult(5, operation ==
                    RankAdjustmentAdminOperation.Reset ? 0 : points, Guid.NewGuid()));
        }
    }

    private sealed class AllowAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId,
            PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class DenyAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId,
            PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
