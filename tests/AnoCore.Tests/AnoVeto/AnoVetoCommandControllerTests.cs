using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Maps;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Modules.AnoVeto;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Maps;
using AnoCore.Runtime.Voting;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
public sealed class AnoVetoCommandControllerTests
{
    private static readonly PlayerId Manager = new(76561198000000101);
    private static readonly PlayerId PlayerA = new(76561198000000102);
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 8, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CreateCommand_OpensNativeEntryMenuWithoutStartingVote()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, hud, players, coordinator, new FixedTimeProvider(Now));

        var result = await commands.ExecuteAsync("!anoveto create", Manager);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(coordinator.TryGetStatus(out _));
        Assert.IsTrue(hud.VisiblePlayers(AnoVetoCreateHudController.HudId).Contains(Manager));
        Assert.IsEmpty(hud.VisiblePlayers(AnoVetoHudController.HudId));
        var definition = hud.Definition(AnoVetoCreateHudController.HudId);
        Assert.IsNotNull(definition);
        Assert.AreEqual(AnoVetoCreateHudController.LayoutResource, definition.LayoutResource);
        Assert.IsTrue(definition.CaptureInput);

        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_random");
        Assert.IsTrue(coordinator.TryGetStatus(out var maps));
        Assert.HasCount(8, maps);
        Assert.IsFalse(hud.VisiblePlayers(AnoVetoCreateHudController.HudId).Contains(Manager));
        Assert.HasCount(2, hud.VisiblePlayers(AnoVetoHudController.HudId));
    }

    [TestMethod]
    public async Task PendingPickerButtons_NeverStartUnconfiguredVote()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, hud, players, coordinator, new FixedTimeProvider(Now));

        Assert.IsTrue((await commands.ExecuteAsync("!anoveto create", Manager)).Success);
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_manual");
        Assert.IsFalse(coordinator.TryGetStatus(out _));
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_saved");
        Assert.IsFalse(coordinator.TryGetStatus(out _));
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_close");
        Assert.IsFalse(hud.VisiblePlayers(AnoVetoCreateHudController.HudId).Contains(Manager));
    }

    [TestMethod]
    public async Task ManualPicker_EightUniqueMapClicksStartSharedVote()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, hud, players, coordinator, new FixedTimeProvider(Now));

        Assert.IsTrue((await commands.ExecuteAsync("!anoveto create", Manager)).Success);
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_manual");
        for (var i = 0; i < 8; i++)
            await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, $"ano_veto_create_map_{i}");

        Assert.IsFalse(coordinator.TryGetStatus(out _));
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_confirm");

        Assert.IsTrue(coordinator.TryGetStatus(out var maps));
        Assert.HasCount(8, maps);
        Assert.HasCount(2, hud.VisiblePlayers(AnoVetoHudController.HudId));
        Assert.IsFalse(hud.VisiblePlayers(AnoVetoCreateHudController.HudId).Contains(Manager));
    }

    [TestMethod]
    public async Task SavedVeto_StoreRenameBrowseAndStartFromPersonalPreset()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        IAnoVetoPresetRepository presets = new AnoVetoPresetCatalog();
        using var controller = new AnoVetoCommandController(
            commands, hud, players, coordinator, new FixedTimeProvider(Now), presets);

        Assert.IsTrue((await commands.ExecuteAsync("!anoveto create", Manager)).Success);
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_manual");
        for (var i = 0; i < 8; i++)
            await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, $"ano_veto_create_map_{i}");
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_save");

        var saved = await presets.ListAsync(Manager.SteamId64);
        Assert.HasCount(1, saved);
        Assert.AreEqual("Veto 01", saved[0].Preset.Name);
        Assert.HasCount(8, saved[0].Preset.MapIds);

        var renamed = await commands.ExecuteAsync("!anovetopreset rename \"Veto 01\" \"Custom Pool\"", Manager);
        Assert.IsTrue(renamed.Success, renamed.Message);
        Assert.IsNotNull(await presets.GetAsync(Manager.SteamId64, "Custom Pool"));
        var listed = await commands.ExecuteAsync("!anovetopreset list", Manager);
        Assert.IsTrue(listed.Success, listed.Message);
        StringAssert.Contains(listed.Message, "Custom Pool");

        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_back");
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_saved");
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_map_0");
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_confirm");
        Assert.IsTrue(coordinator.TryGetStatus(out var maps));
        Assert.HasCount(8, maps);
        Assert.HasCount(2, hud.VisiblePlayers(AnoVetoHudController.HudId));
    }

    [TestMethod]
    public async Task SavedVeto_EditReplacesOneMapAndKeepsOriginalName()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager")]);
        var coordinator = CreateCoordinator(permissions, mapCount: 12);
        IAnoVetoPresetRepository presets = new AnoVetoPresetCatalog();
        await presets.CreateAsync(new AnoVetoPreset(
            Manager.SteamId64, "My Pool", Enumerable.Range(1, 8).Select(i => $"de_map{i:00}")));
        using var controller = new AnoVetoCommandController(
            commands, hud, players, coordinator, new FixedTimeProvider(Now), presets);

        Assert.IsTrue((await commands.ExecuteAsync("!anoveto create", Manager)).Success);
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_saved");
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_map_0");
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_edit");
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_map_0");
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_map_8");
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_save");

        var stored = await presets.GetAsync(Manager.SteamId64, "My Pool");
        Assert.IsNotNull(stored);
        Assert.AreEqual(2L, stored.Value.Revision);
        Assert.HasCount(8, stored.Value.Preset.MapIds);
        CollectionAssert.Contains(stored.Value.Preset.MapIds.ToArray(), "de_map09");
        CollectionAssert.DoesNotContain(stored.Value.Preset.MapIds.ToArray(), "de_map01");
    }

    [TestMethod]
    public async Task SavedVeto_DeleteNeedsSecondConfirmation()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager")]);
        var coordinator = CreateCoordinator(permissions);
        IAnoVetoPresetRepository presets = new AnoVetoPresetCatalog();
        await presets.CreateAsync(new AnoVetoPreset(
            Manager.SteamId64, "Old Veto", Enumerable.Range(1, 8).Select(i => $"de_map{i:00}")));
        using var controller = new AnoVetoCommandController(
            commands, hud, players, coordinator, new FixedTimeProvider(Now), presets);

        Assert.IsTrue((await commands.ExecuteAsync("!anoveto create", Manager)).Success);
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_saved");
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_map_0");
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_delete");
        Assert.HasCount(1, await presets.ListAsync(Manager.SteamId64));
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_delete");
        Assert.HasCount(0, await presets.ListAsync(Manager.SteamId64));
    }

    [TestMethod]
    public async Task RandomCommand_ExplicitlyPreservesLegacyFastStart()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, hud, players, coordinator, new FixedTimeProvider(Now));

        var result = await commands.ExecuteAsync("!anoveto random", Manager);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(coordinator.TryGetStatus(out var maps));
        Assert.HasCount(8, maps);
        Assert.HasCount(2, hud.VisiblePlayers(AnoVetoHudController.HudId));
    }

    [TestMethod]
    public async Task RandomCommand_RequiresSameManagerPermissionAsCreate()
    {
        var permissions = new ManagerPermissionEvaluator(allow: false);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, hud, players, coordinator, new FixedTimeProvider(Now));

        var result = await commands.ExecuteAsync("!anoveto random", Manager);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.IsFalse(coordinator.TryGetStatus(out _));
    }

    [TestMethod]
    public async Task CreateMenu_UnauthorizedRandomClickDoesNotStartVote()
    {
        var permissions = new ManagerPermissionEvaluator(allow: false);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, hud, players, coordinator, new FixedTimeProvider(Now));

        var result = await commands.ExecuteAsync("!anoveto create", Manager);

        Assert.IsTrue(result.Success);
        await hud.ClickAsync(Manager, AnoVetoCreateHudController.HudId, "ano_veto_create_random");
        Assert.IsFalse(coordinator.TryGetStatus(out _));
        Assert.IsEmpty(hud.VisiblePlayers(AnoVetoHudController.HudId));
    }

    [TestMethod]
    public async Task Dispose_UnregistersAnoVetoCommandAndHud()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager")]);
        var coordinator = CreateCoordinator(permissions);
        var controller = new AnoVetoCommandController(commands, hud, players, coordinator, new FixedTimeProvider(Now));

        controller.Dispose();
        var result = await commands.ExecuteAsync("!anoveto create", Manager);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.NotFound, result.FailureReason);
        Assert.IsNull(hud.Definition(AnoVetoHudController.HudId));
        Assert.IsNull(hud.Definition(AnoVetoCreateHudController.HudId));
    }

    [TestMethod]
    public async Task OpenCommand_ReopensCustomHudForPlayer()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, hud, players, coordinator, new FixedTimeProvider(Now));
        Assert.IsTrue((await commands.ExecuteAsync("!anoveto random", Manager)).Success);
        Assert.IsTrue(hud.Hide(PlayerA, AnoVetoHudController.HudId));
        Assert.IsFalse(hud.VisiblePlayers(AnoVetoHudController.HudId).Contains(PlayerA));

        var result = await commands.ExecuteAsync("!anoveto", PlayerA);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(hud.VisiblePlayers(AnoVetoHudController.HudId).Contains(PlayerA));
    }

    [TestMethod]
    public async Task HudSelection_CastsBallotForSelectedMap()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, hud, players, coordinator, new FixedTimeProvider(Now));
        Assert.IsTrue((await commands.ExecuteAsync("!anoveto random", Manager)).Success);

        await hud.ClickAsync(PlayerA, AnoVetoHudController.HudId, "ano_veto_map_3");

        Assert.IsTrue(hud.VisiblePlayers(AnoVetoHudController.HudId).Contains(PlayerA));
        Assert.IsTrue(coordinator.TryGetStatus(out var maps));
        var secondVote = await coordinator.CastAsync(PlayerA, maps[4].MapId, Now.AddSeconds(1));
        Assert.IsTrue(secondVote.Accepted);
        Assert.AreEqual(AnoVetoFailure.AlreadyVoted,
            (await coordinator.CastAsync(PlayerA, maps[3].MapId, Now.AddSeconds(2))).Failure);
        Assert.IsTrue((await coordinator.CastAsync(PlayerA, maps[5].MapId, Now.AddSeconds(3))).Accepted);
        Assert.AreEqual(AnoVetoFailure.AlreadyVoted,
            (await coordinator.CastAsync(PlayerA, maps[6].MapId, Now.AddSeconds(4))).Failure);
    }

    [TestMethod]
    public async Task StatusCommand_IsAvailableToRegularPlayers()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, hud, players, coordinator, new FixedTimeProvider(Now));
        Assert.IsTrue((await commands.ExecuteAsync("!anoveto random", Manager)).Success);

        var result = await commands.ExecuteAsync("!anoveto status", PlayerA);

        Assert.IsTrue(result.Success, result.Message);
        StringAssert.Contains(result.Message, "8");
    }

    [TestMethod]
    public async Task CancelCommand_EndsVoteAndHidesCustomHudForEveryone()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, hud, players, coordinator, new FixedTimeProvider(Now));
        Assert.IsTrue((await commands.ExecuteAsync("!anoveto random", Manager)).Success);
        Assert.HasCount(2, hud.VisiblePlayers(AnoVetoHudController.HudId));

        var result = await commands.ExecuteAsync("!anoveto cancel", Manager);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(coordinator.TryGetStatus(out _));
        Assert.IsEmpty(hud.VisiblePlayers(AnoVetoHudController.HudId));
    }

    [TestMethod]
    public async Task CancelCommand_RejectsUnauthorizedPlayer()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, hud, players, coordinator, new FixedTimeProvider(Now));
        Assert.IsTrue((await commands.ExecuteAsync("!anoveto random", Manager)).Success);

        var result = await commands.ExecuteAsync("!anoveto cancel", PlayerA);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.IsTrue(coordinator.TryGetStatus(out _));
        Assert.HasCount(2, hud.VisiblePlayers(AnoVetoHudController.HudId));
    }

    private static AnoVetoCoordinator CreateCoordinator(IPermissionEvaluator permissions, int mapCount = 8)
    {
        var catalog = new MapCatalog(Enumerable.Range(1, mapCount)
            .Select(index => new MapDefinition($"Map {index:00}", $"de_map{index:00}")));
        return new AnoVetoCoordinator(
            catalog,
            new VoteService(permissions),
            new NoOpMapChanger(),
            new StableRandomSource(),
            new AnoVetoOptions(TimeSpan.FromSeconds(30), 1, VoteTieBreakPolicy.OptionOrder));
    }

    private static PlayerSnapshot Snapshot(PlayerId id, string name)
        => new(
            id,
            PlayerSessionId.New(),
            name,
            isConnected: true,
            isAlive: true,
            PlayerTeam.CounterTerrorist,
            Now,
            Now);

    private sealed class ManagerPermissionEvaluator(bool allow) : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(allow && playerId == Manager);
    }

    private sealed class StubPlayerRegistry(IReadOnlyCollection<PlayerSnapshot> onlinePlayers) : IPlayerRegistry
    {
        public IReadOnlyCollection<PlayerSnapshot> OnlinePlayers { get; } = onlinePlayers;

        public bool TryGet(PlayerId id, out PlayerSnapshot? player)
        {
            player = OnlinePlayers.SingleOrDefault(candidate => candidate.Id == id);
            return player is not null;
        }

        public ValueTask<PlayerSnapshot> ConnectAsync(PlayerConnection connection, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<PlayerSnapshot?> UpdateAsync(PlayerStateUpdate update, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<PlayerSnapshot?> DisconnectAsync(
            PlayerId id,
            PlayerSessionId sessionId,
            DateTimeOffset disconnectedAtUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StableRandomSource : IAnoVetoRandomSource
    {
        public IReadOnlyList<T> Select<T>(IReadOnlyList<T> source, int count) => source.Take(count).ToArray();
    }

    private sealed class NoOpMapChanger : IMapChanger
    {
        public ValueTask ChangeMapAsync(MapDefinition map, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
