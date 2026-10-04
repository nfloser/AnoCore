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
public sealed class AnoVetoCommandLifecycleTests
{
    private static readonly PlayerId Manager = new(76561198000000301);
    private static readonly PlayerId PlayerA = new(76561198000000302);
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 13, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task FinalEligibleHudSelection_HidesVoteHudForEveryone()
    {
        var harness = CreateHarness(TimeSpan.FromMinutes(1));
        using var controller = harness.Controller;
        Assert.IsTrue((await harness.Commands.ExecuteAsync("!anoveto create", Manager)).Success);
        Assert.HasCount(2, harness.Hud.VisiblePlayers(AnoVetoHudController.HudId));

        await harness.Hud.ClickAsync(PlayerA, AnoVetoHudController.HudId, "ano_veto_map_0");
        Assert.IsFalse(harness.Hud.VisiblePlayers(AnoVetoHudController.HudId).Contains(PlayerA));
        Assert.IsTrue(harness.Hud.VisiblePlayers(AnoVetoHudController.HudId).Contains(Manager));
        await harness.Hud.ClickAsync(Manager, AnoVetoHudController.HudId, "ano_veto_map_0");

        Assert.IsFalse(harness.Coordinator.TryGetStatus(out _));
        Assert.IsEmpty(harness.Hud.VisiblePlayers(AnoVetoHudController.HudId));
        Assert.HasCount(1, harness.MapChanger.Changed);
    }

    [TestMethod]
    public async Task ExpireAsync_HidesOpenVoteHud()
    {
        var harness = CreateHarness(TimeSpan.FromSeconds(10));
        using var controller = harness.Controller;
        Assert.IsTrue((await harness.Commands.ExecuteAsync("!anoveto create", Manager)).Success);
        Assert.HasCount(2, harness.Hud.VisiblePlayers(AnoVetoHudController.HudId));

        harness.Time.Advance(TimeSpan.FromSeconds(10));
        var expired = await controller.ExpireAsync();

        Assert.IsNotNull(expired);
        Assert.AreEqual(AnoVetoOutcome.QuorumNotMet, expired.Outcome);
        Assert.IsFalse(harness.Coordinator.TryGetStatus(out _));
        Assert.IsEmpty(harness.Hud.VisiblePlayers(AnoVetoHudController.HudId));
        Assert.HasCount(0, harness.MapChanger.Changed);
    }

    private static Harness CreateHarness(TimeSpan duration)
    {
        var permissions = new ManagerPermissionEvaluator();
        var commands = new CommandRegistry(permissions);
        var hud = new TestCustomHudService();
        var players = new StubPlayerRegistry([Snapshot(Manager, "Manager"), Snapshot(PlayerA, "Player A")]);
        var catalog = new MapCatalog(Enumerable.Range(1, 8)
            .Select(index => new MapDefinition($"Map {index:00}", $"de_map{index:00}")));
        var votes = new VoteService(permissions);
        var changer = new RecordingMapChanger();
        var coordinator = new AnoVetoCoordinator(
            catalog,
            votes,
            changer,
            new StableRandomSource(),
            new AnoVetoOptions(duration, 1, VoteTieBreakPolicy.OptionOrder));
        var time = new MutableTimeProvider(Now);
        var controller = new AnoVetoCommandController(commands, hud, players, coordinator, time);
        return new Harness(commands, hud, coordinator, changer, time, controller);
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

    private sealed record Harness(
        CommandRegistry Commands,
        TestCustomHudService Hud,
        AnoVetoCoordinator Coordinator,
        RecordingMapChanger MapChanger,
        MutableTimeProvider Time,
        AnoVetoCommandController Controller);

    private sealed class ManagerPermissionEvaluator : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(playerId == Manager);
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

    private sealed class RecordingMapChanger : IMapChanger
    {
        public List<MapDefinition> Changed { get; } = [];

        public ValueTask ChangeMapAsync(MapDefinition map, CancellationToken cancellationToken = default)
        {
            Changed.Add(map);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
