using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Maps;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Modules.AnoVeto;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Maps;
using AnoCore.Runtime.Menus;
using AnoCore.Runtime.Voting;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
public sealed class AnoVetoCommandControllerTests
{
    private static readonly PlayerId Manager = new(76561198000000101);
    private static readonly PlayerId PlayerA = new(76561198000000102);
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 8, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CreateCommand_StartsVoteForOnlinePlayers()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var menus = new MenuService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, menus, players, coordinator, new FixedTimeProvider(Now));

        var result = await commands.ExecuteAsync("!anoveto create", Manager);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(coordinator.TryGetStatus(out var maps));
        Assert.HasCount(8, maps);
        var cast = await coordinator.CastAsync(PlayerA, maps[0].MapId, Now.AddSeconds(1));
        Assert.IsTrue(cast.Accepted);
    }

    [TestMethod]
    public async Task CreateCommand_RejectsUnauthorizedPlayer()
    {
        var permissions = new ManagerPermissionEvaluator(allow: false);
        var commands = new CommandRegistry(permissions);
        var menus = new MenuService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, menus, players, coordinator, new FixedTimeProvider(Now));

        var result = await commands.ExecuteAsync("!anoveto create", Manager);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.IsFalse(coordinator.TryGetStatus(out _));
    }

    [TestMethod]
    public async Task Dispose_UnregistersAnoVetoCommand()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var menus = new MenuService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager")]);
        var coordinator = CreateCoordinator(permissions);
        var controller = new AnoVetoCommandController(commands, menus, players, coordinator, new FixedTimeProvider(Now));

        controller.Dispose();
        var result = await commands.ExecuteAsync("!anoveto create", Manager);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.NotFound, result.FailureReason);
    }

    [TestMethod]
    public async Task OpenCommand_OpensEightMapMenu()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var menus = new MenuService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, menus, players, coordinator, new FixedTimeProvider(Now));
        Assert.IsTrue((await commands.ExecuteAsync("!anoveto create", Manager)).Success);

        var result = await commands.ExecuteAsync("!anoveto", PlayerA);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(menus.TryGetOpenMenu(PlayerA, out var menu));
        Assert.IsNotNull(menu);
        Assert.HasCount(8, menu.Options);
        Assert.IsTrue(coordinator.TryGetStatus(out var maps));
        CollectionAssert.AreEqual(maps.Select(map => map.DisplayName).ToArray(), menu.Options.Select(option => option.Label).ToArray());
    }

    [TestMethod]
    public async Task MenuSelection_CastsBallotForSelectedMap()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var menus = new MenuService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, menus, players, coordinator, new FixedTimeProvider(Now));
        Assert.IsTrue((await commands.ExecuteAsync("!anoveto create", Manager)).Success);
        Assert.IsTrue((await commands.ExecuteAsync("!anoveto", PlayerA)).Success);
        Assert.IsTrue(menus.TryGetOpenMenu(PlayerA, out var menu));
        Assert.IsNotNull(menu);

        var selected = menu.Options[3];
        var selection = await menus.SelectAsync(PlayerA, selected.Id);

        Assert.IsTrue(selection.Accepted);
        Assert.IsTrue(coordinator.TryGetStatus(out var maps));
        var secondVote = await coordinator.CastAsync(PlayerA, maps[4].MapId, Now.AddSeconds(1));
        Assert.IsFalse(secondVote.Accepted);
        Assert.AreEqual(AnoVetoFailure.AlreadyVoted, secondVote.Failure);
    }

    [TestMethod]
    public async Task StatusCommand_IsAvailableToRegularPlayers()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var menus = new MenuService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, menus, players, coordinator, new FixedTimeProvider(Now));
        Assert.IsTrue((await commands.ExecuteAsync("!anoveto create", Manager)).Success);

        var result = await commands.ExecuteAsync("!anoveto status", PlayerA);

        Assert.IsTrue(result.Success, result.Message);
        StringAssert.Contains(result.Message, "8");
    }

    [TestMethod]
    public async Task CancelCommand_EndsVoteAndClosesOpenMenus()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var menus = new MenuService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, menus, players, coordinator, new FixedTimeProvider(Now));
        Assert.IsTrue((await commands.ExecuteAsync("!anoveto create", Manager)).Success);
        Assert.IsTrue((await commands.ExecuteAsync("!anoveto", PlayerA)).Success);
        Assert.IsTrue(menus.TryGetOpenMenu(PlayerA, out _));

        var result = await commands.ExecuteAsync("!anoveto cancel", Manager);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(coordinator.TryGetStatus(out _));
        Assert.IsFalse(menus.TryGetOpenMenu(PlayerA, out _));
    }

    [TestMethod]
    public async Task CancelCommand_RejectsUnauthorizedPlayer()
    {
        var permissions = new ManagerPermissionEvaluator(allow: true);
        var commands = new CommandRegistry(permissions);
        var menus = new MenuService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var coordinator = CreateCoordinator(permissions);
        using var controller = new AnoVetoCommandController(commands, menus, players, coordinator, new FixedTimeProvider(Now));
        Assert.IsTrue((await commands.ExecuteAsync("!anoveto create", Manager)).Success);

        var result = await commands.ExecuteAsync("!anoveto cancel", PlayerA);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.Forbidden, result.FailureReason);
        Assert.IsTrue(coordinator.TryGetStatus(out _));
    }

    private static AnoVetoCoordinator CreateCoordinator(IPermissionEvaluator permissions)
    {
        var catalog = new MapCatalog(Enumerable.Range(1, 8)
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
