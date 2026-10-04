using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Commands;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class StatisticsResetCommandControllerTests
{
    private static readonly PlayerId Actor = new(76561198000190101);
    private static readonly PlayerId Target = new(76561198000190102);
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 18, 45, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Command_RoutesTargetReasonActorTimeAndPermission()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        var targets = new StubTargetGateway();
        var administration = new StubAdministrationService();
        using var controller = new StatisticsResetCommandController(
            registry,
            new StatisticsResetCommandExecutor(
                targets, administration, new FixedTimeProvider(Now)));

        var descriptor = registry.GetCommands().Single();
        Assert.AreEqual("anoresetstats", descriptor.Name);
        Assert.AreEqual(new PermissionId("ano.stats.reset"), descriptor.Permission);

        var result = await registry.ExecuteAsync(
            "!anoresetstats Target \"season cleanup\"", Actor);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(Target, administration.Target);
        Assert.AreEqual(Actor, administration.Actor);
        Assert.AreEqual("season cleanup", administration.Reason);
        Assert.AreEqual(Now, administration.ResetAt);
        Assert.AreEqual(new PermissionId("ano.stats.reset"), targets.Permission);

        result = await registry.ExecuteAsync("!anoresetstats Target", Actor);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(StatisticsResetCommandExecutor.DefaultReason,
            administration.Reason);
    }

    [TestMethod]
    public async Task PermissionAndImmunityFailuresNeverMutate()
    {
        var deniedRegistry = new CommandRegistry(new DenyAllPermissions());
        var deniedTargets = new StubTargetGateway();
        var deniedAdministration = new StubAdministrationService();
        using var denied = new StatisticsResetCommandController(
            deniedRegistry,
            new StatisticsResetCommandExecutor(
                deniedTargets, deniedAdministration, new FixedTimeProvider(Now)));

        var result = await deniedRegistry.ExecuteAsync("!anoresetstats Target", Actor);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.AreEqual(0, deniedTargets.Calls);
        Assert.AreEqual(0, deniedAdministration.Calls);

        var registry = new CommandRegistry(new AllowAllPermissions());
        var immuneTargets = new StubTargetGateway
        {
            Result = ModerationTargetResult.Reject(
                ModerationTargetFailure.TargetImmune),
        };
        var administration = new StubAdministrationService();
        using var controller = new StatisticsResetCommandController(
            registry,
            new StatisticsResetCommandExecutor(
                immuneTargets, administration, new FixedTimeProvider(Now)));

        result = await registry.ExecuteAsync("!anoresetstats Target", Actor);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.AreEqual(0, administration.Calls);
    }

    [TestMethod]
    public async Task OfflineResolvedTargetIsAcceptedAndDisposeUnregistersCommand()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        var targets = new StubTargetGateway
        {
            Result = ModerationTargetResult.Success(Target),
        };
        var administration = new StubAdministrationService();
        var controller = new StatisticsResetCommandController(
            registry,
            new StatisticsResetCommandExecutor(
                targets, administration, new FixedTimeProvider(Now)));

        Assert.IsTrue((await registry.ExecuteAsync(
            $"!anoresetstats {Target.SteamId64}", Actor)).Success);
        Assert.AreEqual(Target, administration.Target);

        controller.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await registry.ExecuteAsync("!anoresetstats Target", Actor)).FailureReason);
    }

    private sealed class StubTargetGateway : IModerationTargetGateway
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
                ?? ModerationTargetResult.Success(
                    new PlayerSnapshot(
                        Target,
                        "Target",
                        PlayerTeam.Terrorist,
                        true,
                        Now,
                        Now,
                        new PlayerSessionId(Guid.NewGuid()))));
        }
    }

    private sealed class StubAdministrationService
        : IStatisticsResetAdministrationService
    {
        public int Calls { get; private set; }
        public PlayerId? Target { get; private set; }
        public PlayerId? Actor { get; private set; }
        public string? Reason { get; private set; }
        public DateTimeOffset ResetAt { get; private set; }

        public ValueTask<StatisticsResetResult> ResetAsync(
            PlayerId targetId, PlayerId? actorId, string reason,
            DateTimeOffset resetAtUtc,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Target = targetId;
            Actor = actorId;
            Reason = reason;
            ResetAt = resetAtUtc;
            return ValueTask.FromResult(
                new StatisticsResetResult(null, resetAtUtc, Guid.NewGuid()));
        }
    }

    private sealed class AllowAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId, PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class DenyAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId, PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
