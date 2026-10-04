using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ExtendedInventoryTeamCommandExecutorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 16, 30, 0, TimeSpan.Zero);
    private static readonly PlayerId ActorId = new(76561198000016701);
    private static readonly PlayerId TargetId = new(76561198000016702);

    [TestMethod]
    public void ItemCatalogResolvesKnownAliasesButNotArbitraryEntityNames()
    {
        Assert.IsTrue(AdminItemCatalog.TryResolve("ak", out var ak));
        Assert.AreEqual("weapon_ak47", ak!.ClassName);

        Assert.IsTrue(AdminItemCatalog.TryResolve("weapon_awp", out var awp));
        Assert.AreEqual(AdminItemSlot.Primary, awp!.Slot);

        Assert.IsFalse(AdminItemCatalog.TryResolve("weapon_prop_physics", out _));
        Assert.IsFalse(AdminItemCatalog.TryResolve("ak47_extra", out _));
    }

    [TestMethod]
    public async Task RenameValidationRunsBeforeTargetResolution()
    {
        var targets = new FakeTargets(ModerationTargetResult.Success(Player(true)));
        var executor = CreateExecutor(targets, out _);

        var empty = await executor.ExecuteTargetAsync(
            ExtendedInventoryTeamOperation.Rename,
            ActorId,
            "Target",
            ["   "]);
        var control = await executor.ExecuteTargetAsync(
            ExtendedInventoryTeamOperation.Rename,
            ActorId,
            "Target",
            ["bad\nname"]);
        var tooLong = await executor.ExecuteTargetAsync(
            ExtendedInventoryTeamOperation.Rename,
            ActorId,
            "Target",
            [new string('a', 65)]);

        Assert.IsFalse(empty.Success);
        Assert.IsFalse(control.Success);
        Assert.IsFalse(tooLong.Success);
        Assert.AreEqual(0, targets.Calls);
    }

    [TestMethod]
    public async Task GiveUsesCatalogDefinitionAndRejectsUnknownItem()
    {
        var targets = new FakeTargets(ModerationTargetResult.Success(Player(true)));
        var executor = CreateExecutor(targets, out var transport);

        var invalid = await executor.ExecuteTargetAsync(
            ExtendedInventoryTeamOperation.Give,
            ActorId,
            "Target",
            ["custom_entity"]);
        var valid = await executor.ExecuteTargetAsync(
            ExtendedInventoryTeamOperation.Give,
            ActorId,
            "Target",
            ["ak"]);

        Assert.IsFalse(invalid.Success);
        Assert.IsTrue(valid.Success);
        Assert.AreEqual(1, targets.Calls);
        Assert.AreEqual("weapon_ak47", transport.Given.Single().Item.ClassName);
    }

    [TestMethod]
    public async Task StripAndGiveRequireLivingTarget()
    {
        var executor = CreateExecutor(
            new FakeTargets(ModerationTargetResult.Success(Player(false))),
            out var transport);

        var strip = await executor.ExecuteTargetAsync(
            ExtendedInventoryTeamOperation.Strip,
            ActorId,
            "Target",
            []);
        var give = await executor.ExecuteTargetAsync(
            ExtendedInventoryTeamOperation.Give,
            ActorId,
            "Target",
            ["deagle"]);

        Assert.IsFalse(strip.Success);
        Assert.IsFalse(give.Success);
        Assert.AreEqual(0, transport.Stripped.Count);
        Assert.AreEqual(0, transport.Given.Count);
    }

    [TestMethod]
    public async Task TeamAndSwapUseValidatedTeamSemantics()
    {
        var target = Player(true).WithTeam(PlayerTeam.Terrorist);
        var targets = new FakeTargets(ModerationTargetResult.Success(target));
        var executor = CreateExecutor(targets, out var transport);

        var setSpec = await executor.ExecuteTargetAsync(
            ExtendedInventoryTeamOperation.SetTeam,
            ActorId,
            "Target",
            ["spec"]);
        var swap = await executor.ExecuteTargetAsync(
            ExtendedInventoryTeamOperation.SwapTeam,
            ActorId,
            "Target",
            []);

        Assert.IsTrue(setSpec.Success);
        Assert.IsTrue(swap.Success);
        CollectionAssert.AreEqual(
            new[] { PlayerTeam.Spectator, PlayerTeam.CounterTerrorist },
            transport.Teams.Select(x => x.Team).ToArray());
    }

    [TestMethod]
    public async Task SwapRejectsSpectator()
    {
        var spectator = Player(true).WithTeam(PlayerTeam.Spectator);
        var executor = CreateExecutor(
            new FakeTargets(ModerationTargetResult.Success(spectator)),
            out var transport);

        var result = await executor.ExecuteTargetAsync(
            ExtendedInventoryTeamOperation.SwapTeam,
            ActorId,
            "Target",
            []);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, result.FailureReason);
        Assert.AreEqual(0, transport.Teams.Count);
    }

    [TestMethod]
    public async Task HideRequiresAConnectedPlayerCaller()
    {
        var registry = new FakePlayerRegistry();
        var actor = Player(true).WithId(ActorId);
        registry.Player = actor;
        var transport = new RecordingTransport();
        var executor = new ExtendedInventoryTeamCommandExecutor(
            new FakeTargets(ModerationTargetResult.Success(Player(true))),
            registry,
            new AllowAllPermissions(),
            transport);

        var console = await executor.ExecuteHideAsync(null);
        var hidden = await executor.ExecuteHideAsync(ActorId);

        Assert.IsFalse(console.Success);
        Assert.IsTrue(hidden.Success);
        Assert.AreEqual(actor.SessionId, transport.Hidden.Single().SessionId);
    }

    private static ExtendedInventoryTeamCommandExecutor CreateExecutor(
        FakeTargets targets,
        out RecordingTransport transport)
    {
        transport = new RecordingTransport();
        return new ExtendedInventoryTeamCommandExecutor(
            targets,
            new FakePlayerRegistry(),
            new AllowAllPermissions(),
            transport);
    }

    private static PlayerSnapshot Player(bool alive) => new(
        TargetId,
        PlayerSessionId.New(),
        "Target",
        true,
        alive,
        PlayerTeam.Terrorist,
        Now,
        Now);

    private static PlayerSnapshot WithTeam(
        this PlayerSnapshot player,
        PlayerTeam team)
        => new(
            player.Id,
            player.SessionId,
            player.Name,
            player.IsConnected,
            player.IsAlive,
            team,
            player.ConnectedAtUtc,
            player.LastUpdatedAtUtc);

    private static PlayerSnapshot WithId(
        this PlayerSnapshot player,
        PlayerId id)
        => new(
            id,
            player.SessionId,
            player.Name,
            player.IsConnected,
            player.IsAlive,
            player.Team,
            player.ConnectedAtUtc,
            player.LastUpdatedAtUtc);

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

    private sealed class FakePlayerRegistry : IPlayerRegistry
    {
        public PlayerSnapshot? Player { get; set; }

        public IReadOnlyCollection<PlayerSnapshot> OnlinePlayers
            => Player is null ? [] : [Player];

        public bool TryGet(PlayerId id, out PlayerSnapshot? player)
        {
            player = Player is not null && Player.Id == id ? Player : null;
            return player is not null;
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

    private sealed class AllowAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class RecordingTransport : IExtendedInventoryTeamTransport
    {
        public List<(PlayerSnapshot Player, string Name)> Renamed { get; } = [];
        public List<PlayerSnapshot> Stripped { get; } = [];
        public List<(PlayerSnapshot Player, AdminItemDefinition Item)> Given { get; } = [];
        public List<(PlayerSnapshot Player, PlayerTeam Team)> Teams { get; } = [];
        public List<PlayerSnapshot> Hidden { get; } = [];

        public ValueTask RenameAsync(
            PlayerSnapshot player,
            string name,
            CancellationToken cancellationToken = default)
        {
            Renamed.Add((player, name));
            return ValueTask.CompletedTask;
        }

        public ValueTask StripAsync(
            PlayerSnapshot player,
            CancellationToken cancellationToken = default)
        {
            Stripped.Add(player);
            return ValueTask.CompletedTask;
        }

        public ValueTask GiveAsync(
            PlayerSnapshot player,
            AdminItemDefinition item,
            CancellationToken cancellationToken = default)
        {
            Given.Add((player, item));
            return ValueTask.CompletedTask;
        }

        public ValueTask SetTeamAsync(
            PlayerSnapshot player,
            PlayerTeam team,
            CancellationToken cancellationToken = default)
        {
            Teams.Add((player, team));
            return ValueTask.CompletedTask;
        }

        public ValueTask HideAsync(
            PlayerSnapshot player,
            CancellationToken cancellationToken = default)
        {
            Hidden.Add(player);
            return ValueTask.CompletedTask;
        }
    }
}
