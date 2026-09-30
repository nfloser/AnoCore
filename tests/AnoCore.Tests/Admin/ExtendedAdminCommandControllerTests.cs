using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Targeting;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Commands;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ExtendedAdminCommandControllerTests
{
    private static readonly PlayerId ActorId = new(76561198000004101);
    private static readonly PlayerId TargetId = new(76561198000004102);
    private static readonly PlayerId DestinationId = new(76561198000004103);
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void RegistersTheCompleteExtendedAdminSurface()
    {
        var fixture = CreateFixture();

        using var controller = fixture.CreateController();

        var commands = fixture.Registry.GetCommands()
            .OrderBy(command => command.Name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "anoantighosting",
                "anoarmor",
                "anoblind",
                "anobury",
                "anocvar",
                "anofreeze",
                "anogive",
                "anogod",
                "anohide",
                "anohealth",
                "anonoclip",
                "anorcon",
                "anorename",
                "anorespawn",
                "anorevive",
                "anoslap",
                "anoslay",
                "anospeed",
                "anostrip",
                "anoswap",
                "anoteam",
                "anotp",
                "anotppos",
                "anounblind",
                "anounbury",
                "anounfreeze",
            },
            commands.Select(command => command.Name).ToArray());

        AssertAliases(commands, "anohealth", "anohp");
        AssertAliases(commands, "anoslay", "anokill");
        AssertAliases(commands, "anotp", "anoteleport", "anogoto");
        AssertAliases(commands, "anocvar", "anoconvar");
        AssertAliases(commands, "anohide", "anostealth");
        AssertAliases(commands, "anoantighosting", "anoghosting");
    }

    [TestMethod]
    public async Task HealthUsesDefaultAndCentralTargetAuthorization()
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        var result = await fixture.Registry.ExecuteAsync("!anohealth Target", ActorId);

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(ExtendedAdminOperation.Health, fixture.Transport.LastPlayerRequest?.Operation);
        Assert.AreEqual(100, fixture.Transport.LastPlayerRequest?.IntegerValue);
        Assert.AreEqual(TargetId, fixture.Transport.LastPlayerRequest?.Target.Id);
        Assert.AreEqual(new PermissionId("ano.admin.health"), fixture.Authorization.LastPermission);
        Assert.IsFalse(fixture.Authorization.LastAllowSelf);
    }

    [TestMethod]
    public async Task ArmorAcceptsExplicitBoundedValue()
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        var result = await fixture.Registry.ExecuteAsync("!anoarmor Target 250", ActorId);

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(ExtendedAdminOperation.Armor, fixture.Transport.LastPlayerRequest?.Operation);
        Assert.AreEqual(250, fixture.Transport.LastPlayerRequest?.IntegerValue);
    }

    [DataTestMethod]
    [DataRow("!anofreeze Target", ExtendedAdminOperation.Freeze)]
    [DataRow("!anounfreeze Target", ExtendedAdminOperation.Unfreeze)]
    [DataRow("!anoslay Target", ExtendedAdminOperation.Slay)]
    [DataRow("!anostrip Target", ExtendedAdminOperation.Strip)]
    [DataRow("!anobury Target", ExtendedAdminOperation.Bury)]
    [DataRow("!anounbury Target", ExtendedAdminOperation.Unbury)]
    [DataRow("!anounblind Target", ExtendedAdminOperation.Unblind)]
    [DataRow("!anogod Target", ExtendedAdminOperation.God)]
    [DataRow("!anoswap Target", ExtendedAdminOperation.Swap)]
    public async Task SimpleAliveTargetCommandsMapToTransport(
        string invocation,
        ExtendedAdminOperation expectedOperation)
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        var result = await fixture.Registry.ExecuteAsync(invocation, ActorId);

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(expectedOperation, fixture.Transport.LastPlayerRequest?.Operation);
        Assert.AreEqual(TargetId, fixture.Transport.LastPlayerRequest?.Target.Id);
    }

    [TestMethod]
    public async Task RespawnAndReviveRequireDeadTarget()
    {
        var fixture = CreateFixture(targetAlive: false);
        using var controller = fixture.CreateController();

        var respawn = await fixture.Registry.ExecuteAsync("!anorespawn Target", ActorId);
        Assert.IsTrue(respawn.Success, respawn.Message);
        Assert.AreEqual(ExtendedAdminOperation.Respawn, fixture.Transport.LastPlayerRequest?.Operation);

        var revive = await fixture.Registry.ExecuteAsync("!anorevive Target", ActorId);
        Assert.IsTrue(revive.Success, revive.Message);
        Assert.AreEqual(ExtendedAdminOperation.Revive, fixture.Transport.LastPlayerRequest?.Operation);
    }

    [TestMethod]
    public async Task RespawnRejectsAliveTargetBeforeNativeWork()
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        var result = await fixture.Registry.ExecuteAsync("!anorespawn Target", ActorId);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, result.FailureReason);
        Assert.IsNull(fixture.Transport.LastPlayerRequest);
    }

    [TestMethod]
    public async Task NoclipIsSelfOnlyAndCannotRunFromServerConsole()
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        var fromConsole = await fixture.Registry.ExecuteAsync("!anonoclip", null);
        Assert.IsFalse(fromConsole.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, fromConsole.FailureReason);

        var fromPlayer = await fixture.Registry.ExecuteAsync("!anonoclip", ActorId);
        Assert.IsTrue(fromPlayer.Success, fromPlayer.Message);
        Assert.AreEqual(ExtendedAdminOperation.Noclip, fixture.Transport.LastPlayerRequest?.Operation);
        Assert.AreEqual(ActorId, fixture.Transport.LastPlayerRequest?.Target.Id);
        Assert.IsTrue(fixture.Authorization.LastAllowSelf);
        Assert.AreEqual(new PermissionId("ano.admin.noclip"), fixture.Authorization.LastPermission);
    }

    [TestMethod]
    public async Task HideIsSelfOnly()
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        var result = await fixture.Registry.ExecuteAsync("!anohide", ActorId);

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(ExtendedAdminOperation.Hide, fixture.Transport.LastPlayerRequest?.Operation);
        Assert.AreEqual(ActorId, fixture.Transport.LastPlayerRequest?.Target.Id);
        Assert.IsTrue(fixture.Authorization.LastAllowSelf);
    }

    [TestMethod]
    public async Task RenameRejectsControlCharactersAndForwardsCleanName()
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        var invalid = await fixture.Registry.ExecuteAsync("!anorename Target \"bad\\nname\"", ActorId);
        Assert.IsFalse(invalid.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, invalid.FailureReason);
        Assert.IsNull(fixture.Transport.LastPlayerRequest);

        var valid = await fixture.Registry.ExecuteAsync("!anorename Target \"New Name\"", ActorId);
        Assert.IsTrue(valid.Success, valid.Message);
        Assert.AreEqual(ExtendedAdminOperation.Rename, fixture.Transport.LastPlayerRequest?.Operation);
        Assert.AreEqual("New Name", fixture.Transport.LastPlayerRequest?.TextValue);
    }

    [DataTestMethod]
    [DataRow("!anogive Target ak47", "ak47")]
    [DataRow("!anogive Target weapon_m4a1", "weapon_m4a1")]
    [DataRow("!anogive Target item_assaultsuit", "item_assaultsuit")]
    public async Task GiveAcceptsSafeDesignerTokens(string invocation, string expected)
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        var result = await fixture.Registry.ExecuteAsync(invocation, ActorId);

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(ExtendedAdminOperation.Give, fixture.Transport.LastPlayerRequest?.Operation);
        Assert.AreEqual(expected, fixture.Transport.LastPlayerRequest?.TextValue);
    }

    [TestMethod]
    public async Task GiveRejectsCommandInjectionTokens()
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        var result = await fixture.Registry.ExecuteAsync("!anogive Target \"ak47;quit\"", ActorId);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, result.FailureReason);
        Assert.IsNull(fixture.Transport.LastPlayerRequest);
    }

    [TestMethod]
    public async Task TeleportPositionParsesInvariantFiniteCoordinates()
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        var result = await fixture.Registry.ExecuteAsync("!anotppos Target 100.5 -20 42.25", ActorId);

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(ExtendedAdminOperation.TeleportPosition, fixture.Transport.LastPlayerRequest?.Operation);
        Assert.AreEqual(new AdminPosition(100.5f, -20f, 42.25f), fixture.Transport.LastPlayerRequest?.Position);
    }

    [TestMethod]
    public async Task TeleportPlayerResolvesBothCurrentSessions()
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        var result = await fixture.Registry.ExecuteAsync("!anotp Target Destination", ActorId);

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(ExtendedAdminOperation.TeleportPlayer, fixture.Transport.LastPlayerRequest?.Operation);
        Assert.AreEqual(TargetId, fixture.Transport.LastPlayerRequest?.Target.Id);
        Assert.AreEqual(DestinationId, fixture.Transport.LastPlayerRequest?.Destination?.Id);
    }

    [TestMethod]
    public async Task SpeedRejectsNonFiniteAndOutOfRangeValues()
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        foreach (var invocation in new[]
        {
            "!anospeed Target NaN",
            "!anospeed Target Infinity",
            "!anospeed Target 0",
            "!anospeed Target 100",
        })
        {
            var result = await fixture.Registry.ExecuteAsync(invocation, ActorId);
            Assert.IsFalse(result.Success, invocation);
            Assert.AreEqual(CommandFailureReason.InvalidInput, result.FailureReason, invocation);
        }

        var valid = await fixture.Registry.ExecuteAsync("!anospeed Target 1.5", ActorId);
        Assert.IsTrue(valid.Success, valid.Message);
        Assert.AreEqual(1.5f, fixture.Transport.LastPlayerRequest?.FloatValue);
    }

    [TestMethod]
    public async Task SlapAndBlindValidateOptionalNumericValues()
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        var slap = await fixture.Registry.ExecuteAsync("!anoslap Target 25", ActorId);
        Assert.IsTrue(slap.Success, slap.Message);
        Assert.AreEqual(25, fixture.Transport.LastPlayerRequest?.IntegerValue);

        var blind = await fixture.Registry.ExecuteAsync("!anoblind Target 3.5", ActorId);
        Assert.IsTrue(blind.Success, blind.Message);
        Assert.AreEqual(3.5f, fixture.Transport.LastPlayerRequest?.FloatValue);

        var invalid = await fixture.Registry.ExecuteAsync("!anoblind Target -1", ActorId);
        Assert.IsFalse(invalid.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, invalid.FailureReason);
    }

    [DataTestMethod]
    [DataRow("t", PlayerTeam.Terrorist)]
    [DataRow("terrorist", PlayerTeam.Terrorist)]
    [DataRow("ct", PlayerTeam.CounterTerrorist)]
    [DataRow("counterterrorist", PlayerTeam.CounterTerrorist)]
    [DataRow("spec", PlayerTeam.Spectator)]
    [DataRow("spectator", PlayerTeam.Spectator)]
    public async Task TeamAcceptsKnownNames(string teamName, PlayerTeam expected)
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        var result = await fixture.Registry.ExecuteAsync($"!anoteam Target {teamName}", ActorId);

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(expected, fixture.Transport.LastPlayerRequest?.Team);
    }

    [TestMethod]
    public async Task PermissionOrImmunityFailureStopsBeforeTransport()
    {
        var fixture = CreateFixture();
        fixture.Authorization.Decision =
            TargetAuthorizationDecision.Denied(TargetAuthorizationFailure.TargetImmune);
        using var controller = fixture.CreateController();

        var result = await fixture.Registry.ExecuteAsync("!anoslay Target", ActorId);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.IsNull(fixture.Transport.LastPlayerRequest);
    }

    [TestMethod]
    public async Task CvarAndServerCommandRejectLineOrCommandChainingInjection()
    {
        var fixture = CreateFixture();
        using var controller = fixture.CreateController();

        var cvar = await fixture.Registry.ExecuteAsync("!anocvar mp_roundtime 2", ActorId);
        Assert.IsTrue(cvar.Success, cvar.Message);
        Assert.AreEqual(("mp_roundtime", "2"), fixture.Transport.LastConVar);

        var command = await fixture.Registry.ExecuteAsync("!anorcon \"mp_restartgame 1\"", ActorId);
        Assert.IsTrue(command.Success, command.Message);
        Assert.AreEqual("mp_restartgame 1", fixture.Transport.LastServerCommand);

        var chained = await fixture.Registry.ExecuteAsync("!anorcon \"mp_restartgame 1;quit\"", ActorId);
        Assert.IsFalse(chained.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, chained.FailureReason);

        var injectedCvar = await fixture.Registry.ExecuteAsync("!anocvar mp_roundtime \"2;quit\"", ActorId);
        Assert.IsFalse(injectedCvar.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, injectedCvar.FailureReason);
    }

    [TestMethod]
    public async Task SameIpInspectionIsPermissionedAndFormatsBoundedResult()
    {
        var fixture = CreateFixture();
        fixture.Transport.SharedIpGroups =
        [
            new SharedIpGroup(
                "203.0.113.10",
                [
                    new SharedIpPlayer(TargetId, "Target"),
                    new SharedIpPlayer(DestinationId, "Destination"),
                ]),
        ];
        using var controller = fixture.CreateController();

        var result = await fixture.Registry.ExecuteAsync("!anoantighosting", ActorId);

        Assert.IsTrue(result.Success, result.Message);
        StringAssert.Contains(result.Message, "203.0.113.10");
        StringAssert.Contains(result.Message, "Target");
        StringAssert.Contains(result.Message, TargetId.SteamId64.ToString());
    }

    [TestMethod]
    public async Task NativeFailureDoesNotReportSuccess()
    {
        var fixture = CreateFixture();
        fixture.Transport.PlayerResult =
            ExtendedAdminTransportResult.Fail(ExtendedAdminTransportFailure.StaleTarget);
        using var controller = fixture.CreateController();

        var result = await fixture.Registry.ExecuteAsync("!anofreeze Target", ActorId);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, result.FailureReason);
    }

    [TestMethod]
    public void RegistrationCollisionRollsBackEarlierCommands()
    {
        var fixture = CreateFixture();
        fixture.Registry.Register(
            new AnoCore.Abstractions.Modules.ModuleId("collision"),
            new CommandDescriptor("anospeed", "occupied"),
            _ => ValueTask.FromResult(CommandResult.Ok()));

        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.CreateController());

        CollectionAssert.AreEqual(
            new[] { "anospeed" },
            fixture.Registry.GetCommands().Select(command => command.Name).ToArray());
    }

    [TestMethod]
    public void DisposeRemovesEveryExtendedCommand()
    {
        var fixture = CreateFixture();
        var controller = fixture.CreateController();

        controller.Dispose();

        Assert.AreEqual(0, fixture.Registry.GetCommands().Count);
    }

    private static Fixture CreateFixture(bool targetAlive = true)
    {
        var actor = Snapshot(ActorId, "Actor", true, PlayerTeam.CounterTerrorist, "actor-session");
        var target = Snapshot(TargetId, "Target", targetAlive, PlayerTeam.Terrorist, "target-session");
        var destination = Snapshot(
            DestinationId,
            "Destination",
            true,
            PlayerTeam.CounterTerrorist,
            "destination-session");

        return new Fixture(actor, target, destination);
    }

    private static PlayerSnapshot Snapshot(
        PlayerId id,
        string name,
        bool alive,
        PlayerTeam team,
        string session)
        => new(
            id,
            new PlayerSessionId(session),
            name,
            true,
            alive,
            team,
            Now,
            Now);

    private static void AssertAliases(
        IReadOnlyCollection<CommandDescriptor> commands,
        string name,
        params string[] expected)
    {
        var descriptor = commands.Single(command => command.Name == name);
        CollectionAssert.AreEquivalent(expected, descriptor.Aliases.ToArray());
    }

    private sealed class Fixture
    {
        public Fixture(
            PlayerSnapshot actor,
            PlayerSnapshot target,
            PlayerSnapshot destination)
        {
            Registry = new CommandRegistry(new AllowAllPermissions());
            Players = new StubPlayerRegistry([actor, target, destination]);
            Resolver = new StubTargetResolver(
                new Dictionary<string, PlayerSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Actor"] = actor,
                    ["Target"] = target,
                    ["Destination"] = destination,
                });
            Authorization = new StubTargetAuthorization();
            Transport = new StubExtendedAdminTransport();
        }

        public CommandRegistry Registry { get; }

        public StubPlayerRegistry Players { get; }

        public StubTargetResolver Resolver { get; }

        public StubTargetAuthorization Authorization { get; }

        public StubExtendedAdminTransport Transport { get; }

        public ExtendedAdminCommandController CreateController()
            => new(
                Registry,
                new ExtendedAdminCommandExecutor(
                    Players,
                    Resolver,
                    Authorization,
                    Transport));
    }

    private sealed class AllowAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class StubPlayerRegistry(IReadOnlyCollection<PlayerSnapshot> players) : IPlayerRegistry
    {
        private readonly Dictionary<PlayerId, PlayerSnapshot> _players =
            players.ToDictionary(player => player.Id);

        public IReadOnlyCollection<PlayerSnapshot> OnlinePlayers => _players.Values.ToArray();

        public bool TryGet(PlayerId id, out PlayerSnapshot? player)
            => _players.TryGetValue(id, out player);

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

    private sealed class StubTargetResolver(
        IReadOnlyDictionary<string, PlayerSnapshot> targets) : IPlayerTargetResolver
    {
        public TargetResolutionResult Resolve(
            string selector,
            PlayerId? caller = null,
            TargetSelectorCapabilities capabilities = TargetSelectorCapabilities.None)
            => targets.TryGetValue(selector, out var target)
                ? TargetResolutionResult.Success([target])
                : TargetResolutionResult.Reject(TargetResolutionFailure.NotFound);
    }

    private sealed class StubTargetAuthorization : ITargetAuthorizationService
    {
        public TargetAuthorizationDecision Decision { get; set; } = TargetAuthorizationDecision.Allowed;

        public PermissionId? LastPermission { get; private set; }

        public bool LastAllowSelf { get; private set; }

        public ValueTask<TargetAuthorizationDecision> AuthorizeAsync(
            PlayerId actor,
            PlayerSnapshot target,
            PermissionId permission,
            bool allowSelf = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastPermission = permission;
            LastAllowSelf = allowSelf;
            return ValueTask.FromResult(Decision);
        }
    }

    private sealed class StubExtendedAdminTransport : IExtendedAdminTransport
    {
        public ExtendedAdminPlayerRequest? LastPlayerRequest { get; private set; }

        public (string Name, string Value)? LastConVar { get; private set; }

        public string? LastServerCommand { get; private set; }

        public ExtendedAdminTransportResult PlayerResult { get; set; } =
            ExtendedAdminTransportResult.Ok();

        public IReadOnlyList<SharedIpGroup> SharedIpGroups { get; set; } = [];

        public ValueTask<ExtendedAdminTransportResult> ExecutePlayerAsync(
            ExtendedAdminPlayerRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastPlayerRequest = request;
            return ValueTask.FromResult(PlayerResult);
        }

        public ValueTask<ExtendedAdminTransportResult> SetConVarAsync(
            string name,
            string value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastConVar = (name, value);
            return ValueTask.FromResult(ExtendedAdminTransportResult.Ok());
        }

        public ValueTask<ExtendedAdminTransportResult> ExecuteServerCommandAsync(
            string command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastServerCommand = command;
            return ValueTask.FromResult(ExtendedAdminTransportResult.Ok());
        }

        public ValueTask<IReadOnlyList<SharedIpGroup>> GetSharedIpGroupsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(SharedIpGroups);
        }
    }
}
