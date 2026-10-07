using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using AnoCore.Modules.Progression;
using AnoCore.Runtime.Commands;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class ProgressionAdministrationTests
{
    private static readonly PlayerId Player = new(76561198000293101);
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 13, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void PolicyRejectsInvalidRequestsAndUnderflowOverflow()
    {
        var request = new ProgressionAdminRequest(Guid.NewGuid(), ProgressionAdminOperation.Give, Player, 100, null, "Correction", Now);
        Assert.ThrowsExactly<ArgumentException>(() => ProgressionAdministrationPolicy.Validate(request with { RequestId = Guid.Empty }));
        Assert.ThrowsExactly<ArgumentException>(() => ProgressionAdministrationPolicy.Validate(request with { Amount = 0 }));
        Assert.ThrowsExactly<ArgumentException>(() => ProgressionAdministrationPolicy.Validate(request with { Amount = -1 }));
        Assert.ThrowsExactly<ArgumentException>(() => ProgressionAdministrationPolicy.Validate(request with { Amount = 1_000_000_001 }));
        Assert.ThrowsExactly<ArgumentException>(() => ProgressionAdministrationPolicy.Validate(request with { Reason = "bad\nreason" }));
        Assert.ThrowsExactly<ArgumentException>(() => ProgressionAdministrationPolicy.Validate(request with { Reason = new('x', 385) }));
        Assert.ThrowsExactly<ArgumentException>(() => ProgressionAdministrationPolicy.Validate(request with { Operation = ProgressionAdminOperation.Reset }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ProgressionAdministrationPolicy.Calculate(ProgressionAdminOperation.Take, 0, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ProgressionAdministrationPolicy.Calculate(ProgressionAdminOperation.Give, long.MaxValue, 1));
        Assert.AreEqual(Now, ProgressionAdministrationPolicy.Validate(request with { OccurredAtUtc = Now.ToOffset(TimeSpan.FromHours(2)) }).OccurredAtUtc);
    }

    [TestMethod]
    public async Task CommandsUseCentralTargetsDistinctPermissionsAndAuditRequests()
    {
        var commands = new CommandRegistry(new Permissions());
        var gateway = new Targets();
        var service = new Administration();
        using var controller = new ProgressionAdminCommandController(commands, gateway, service, () => Now);
        foreach (var command in new[] { "!anogivexp 76561198000293101 10 Correction", "!anotakexp 76561198000293101 5 Correction",
            "!anosetxp 76561198000293101 20 Correction", "!anoresetxp 76561198000293101 Correction" })
            Assert.IsTrue((await commands.ExecuteAsync(command, null)).Success);
        Assert.HasCount(4, service.Requests);
        Assert.HasCount(4, service.Requests.Select(item => item.RequestId).Distinct().ToArray());
        CollectionAssert.AreEqual(new[] { "ano.progression.xp.give", "ano.progression.xp.take", "ano.progression.xp.set", "ano.progression.xp.reset" }, gateway.Permissions);
        Assert.IsTrue(service.Requests.All(item => item.Target == Player && item.Actor is null && item.Reason == "Correction" && item.OccurredAtUtc == Now));
        controller.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound, (await commands.ExecuteAsync("!anogivexp 76561198000293101 10 Correction", null)).FailureReason);
    }

    [TestMethod]
    public async Task PermissionAndImmunityDenialNeverMutateXp()
    {
        var service = new Administration();
        var commands = new CommandRegistry(new Permissions { Allow = false });
        var gateway = new Targets();
        using var denied = new ProgressionAdminCommandController(commands, gateway, service);
        Assert.IsFalse((await commands.ExecuteAsync("!anogivexp target 10 Correction", Player)).Success);
        Assert.IsEmpty(gateway.Permissions);
        commands = new CommandRegistry(new Permissions());
        gateway = new Targets { Failure = ModerationTargetFailure.TargetImmune };
        using var immune = new ProgressionAdminCommandController(commands, gateway, service);
        Assert.AreEqual(CommandFailureReason.Forbidden, (await commands.ExecuteAsync("!anogivexp target 10 Correction", null)).FailureReason);
        Assert.IsEmpty(service.Requests);
    }

    [TestMethod]
    public async Task InvalidAmountReturnsFailureAndRegistrationCollisionRollsBack()
    {
        var commands = new CommandRegistry(new Permissions());
        using var reserved = commands.Register(new ModuleId("reserved"), new("anosetxp", "Reserved"), _ => ValueTask.FromResult(CommandResult.Ok()));
        Assert.ThrowsExactly<InvalidOperationException>(() => new ProgressionAdminCommandController(commands, new Targets(), new Administration()));
        Assert.HasCount(1, commands.GetCommands());
        var clean = new CommandRegistry(new Permissions());
        using var controller = new ProgressionAdminCommandController(clean, new Targets(), new Administration());
        Assert.AreEqual(CommandFailureReason.InvalidInput, (await clean.ExecuteAsync("!anogivexp target -1 Correction", null)).FailureReason);
    }

    private sealed class Permissions : IPermissionEvaluator
    {
        public bool Allow { get; init; } = true;
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Allow);
    }

    private sealed class Targets : IModerationTargetGateway
    {
        public ModerationTargetFailure Failure { get; init; }
        public List<string> Permissions { get; } = [];
        public ValueTask<ModerationTargetResult> ResolveAsync(string selector, PlayerId? actor, PermissionId permission, CancellationToken cancellationToken = default)
        {
            Permissions.Add(permission.Value);
            return ValueTask.FromResult(Failure == ModerationTargetFailure.None ? ModerationTargetResult.Success(Player) : ModerationTargetResult.Reject(Failure));
        }
    }

    private sealed class Administration : IProgressionAdministrationService
    {
        public List<ProgressionAdminRequest> Requests { get; } = [];
        public ValueTask<ProgressionAdminResult> ApplyAsync(ProgressionAdminRequest request, CancellationToken cancellationToken = default)
        {
            request = ProgressionAdministrationPolicy.Validate(request);
            Requests.Add(request);
            return ValueTask.FromResult(new ProgressionAdminResult(true, request.RequestId, 0, request.Amount, 1));
        }
    }
}
