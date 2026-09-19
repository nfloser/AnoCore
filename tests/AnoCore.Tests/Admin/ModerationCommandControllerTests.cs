using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Commands;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ModerationCommandControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 14, 30, 0, TimeSpan.Zero);
    private static readonly PlayerId Actor = new(76561198000003201);
    private static readonly PlayerId Target = new(76561198000003202);

    [TestMethod]
    public void Constructor_RegistersAllModerationCommandsWithExplicitPermissions()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        using var controller = CreateController(registry, out _, out _);

        var commands = registry.GetCommands().ToDictionary(command => command.Name);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "anoban",
                "anounban",
                "anomute",
                "anounmute",
                "anogag",
                "anoungag",
                "anosilence",
                "anounsilence",
            },
            commands.Keys.ToArray());

        Assert.AreEqual(new PermissionId("ano.admin.ban"), commands["anoban"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.unban"), commands["anounban"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.mute"), commands["anomute"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.unmute"), commands["anounmute"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.gag"), commands["anogag"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.ungag"), commands["anoungag"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.silence"), commands["anosilence"].Permission);
        Assert.AreEqual(new PermissionId("ano.admin.unsilence"), commands["anounsilence"].Permission);

        CollectionAssert.AreEqual(
            new[] { CommandArgumentKind.String, CommandArgumentKind.Int32, CommandArgumentKind.String },
            commands["anoban"].Arguments.Select(argument => argument.Kind).ToArray());
        Assert.IsFalse(commands["anoban"].Arguments[2].Required);
        Assert.AreEqual(2, commands["anounban"].Arguments.Count);
        Assert.IsFalse(commands["anounban"].Arguments[1].Required);
    }

    [TestMethod]
    public async Task BanCommand_ParsesQuotedReasonAndRoutesApplyOperation()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        using var controller = CreateController(registry, out _, out var moderation);

        var result = await registry.ExecuteAsync(
            "!anoban Target 30 \"repeated griefing\"",
            null);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, moderation.ApplyCalls);
        Assert.AreEqual(0, moderation.RevokeCalls);
        Assert.AreEqual(Target, moderation.LastTarget);
        Assert.AreEqual(ModerationRestriction.Connect, moderation.LastRestrictions);
        Assert.AreEqual("repeated griefing", moderation.LastReason);
        Assert.AreEqual(Now.AddMinutes(30), moderation.LastExpiresAtUtc);
    }

    [TestMethod]
    public async Task UnmuteCommand_RoutesRevokeWithoutDuration()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        using var controller = CreateController(registry, out _, out var moderation);

        var result = await registry.ExecuteAsync(
            "!anounmute Target \"manual clear\"",
            null);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, moderation.ApplyCalls);
        Assert.AreEqual(1, moderation.RevokeCalls);
        Assert.AreEqual(ModerationRestriction.Voice, moderation.LastRestrictions);
        Assert.AreEqual("manual clear", moderation.LastReason);
    }

    [TestMethod]
    public async Task RegistryPermissionGateRunsBeforeModerationMutation()
    {
        var registry = new CommandRegistry(new DenyAllPermissions());
        using var controller = CreateController(registry, out var targets, out var moderation);

        var result = await registry.ExecuteAsync(
            "!anomute Target 10 reason",
            Actor);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.AreEqual(0, targets.Calls);
        Assert.AreEqual(0, moderation.ApplyCalls);
        Assert.AreEqual(0, moderation.RevokeCalls);
    }

    [TestMethod]
    public void Constructor_RollsBackEarlierRegistrationsWhenLaterCommandCollides()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        registry.Register(
            new ModuleId("collision"),
            new CommandDescriptor("anogag", "pre-existing"),
            _ => ValueTask.FromResult(CommandResult.Ok()));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CreateController(registry, out _, out _));

        CollectionAssert.AreEqual(
            new[] { "anogag" },
            registry.GetCommands().Select(command => command.Name).ToArray());
    }

    [TestMethod]
    public async Task Dispose_UnregistersEveryOwnedModerationCommand()
    {
        var registry = new CommandRegistry(new AllowAllPermissions());
        var controller = CreateController(registry, out _, out _);

        controller.Dispose();

        Assert.AreEqual(0, registry.GetCommands().Count);
        var result = await registry.ExecuteAsync("!anoban Target 10 reason", null);
        Assert.AreEqual(CommandFailureReason.NotFound, result.FailureReason);
    }

    private static ModerationCommandController CreateController(
        IAnoCommandRegistry registry,
        out StubTargetGateway targets,
        out StubModerationService moderation)
    {
        targets = new StubTargetGateway(Target);
        moderation = new StubModerationService();
        var executor = new ModerationCommandExecutor(
            targets,
            moderation,
            new FixedTimeProvider(Now));
        return new ModerationCommandController(registry, executor);
    }

    private sealed class StubTargetGateway(PlayerId targetId) : IModerationTargetGateway
    {
        public int Calls { get; private set; }

        public ValueTask<ModerationTargetResult> ResolveAsync(
            string selector,
            PlayerId? actor,
            PermissionId permission,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return ValueTask.FromResult(ModerationTargetResult.Success(targetId));
        }
    }

    private sealed class StubModerationService : IModerationService
    {
        public int ApplyCalls { get; private set; }

        public int RevokeCalls { get; private set; }

        public PlayerId? LastTarget { get; private set; }

        public ModerationRestriction LastRestrictions { get; private set; }

        public string? LastReason { get; private set; }

        public DateTimeOffset? LastExpiresAtUtc { get; private set; }

        public ValueTask<IReadOnlyList<ModerationSanction>> ApplyAsync(
            PlayerId targetId,
            PlayerId? actorId,
            ModerationRestriction restrictions,
            string reason,
            DateTimeOffset atUtc,
            DateTimeOffset? expiresAtUtc = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCalls++;
            LastTarget = targetId;
            LastRestrictions = restrictions;
            LastReason = reason;
            LastExpiresAtUtc = expiresAtUtc;
            IReadOnlyList<ModerationSanction> sanctions =
            [
                new(
                    Guid.NewGuid(),
                    targetId,
                    actorId,
                    SingleRestriction(restrictions),
                    reason,
                    atUtc,
                    expiresAtUtc),
            ];
            return ValueTask.FromResult(sanctions);
        }

        public ValueTask<IReadOnlyList<ModerationSanction>> RevokeAsync(
            PlayerId targetId,
            PlayerId? actorId,
            ModerationRestriction restrictions,
            string reason,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RevokeCalls++;
            LastTarget = targetId;
            LastRestrictions = restrictions;
            LastReason = reason;
            var original = new ModerationSanction(
                Guid.NewGuid(),
                targetId,
                actorId,
                SingleRestriction(restrictions),
                reason,
                atUtc);
            IReadOnlyList<ModerationSanction> sanctions =
                [original.Revoke(actorId, reason, atUtc.AddTicks(1))];
            return ValueTask.FromResult(sanctions);
        }

        public ValueTask<ModerationState> GetStateAsync(
            PlayerId targetId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ModerationSanction>> GetHistoryAsync(
            PlayerId targetId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ModerationAuditEntry>> GetAuditHistoryAsync(
            PlayerId targetId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private static ModerationRestriction SingleRestriction(ModerationRestriction restrictions)
            => restrictions.HasFlag(ModerationRestriction.Connect)
                ? ModerationRestriction.Connect
                : restrictions.HasFlag(ModerationRestriction.Voice)
                    ? ModerationRestriction.Voice
                    : ModerationRestriction.Chat;
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
