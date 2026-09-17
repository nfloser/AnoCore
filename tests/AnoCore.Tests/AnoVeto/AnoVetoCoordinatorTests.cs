using AnoCore.Abstractions.Maps;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Modules.AnoVeto;
using AnoCore.Runtime.Maps;
using AnoCore.Runtime.Voting;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
public sealed class AnoVetoCoordinatorTests
{
    private static readonly PlayerId Manager = new(76561198000000001);
    private static readonly PlayerId PlayerA = new(76561198000000002);
    private static readonly PlayerId PlayerB = new(76561198000000003);
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 20, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CreateAsync_SelectsExactlyEightUniqueMapsDeterministically()
    {
        var harness = CreateHarness(10, minimumVotes: 1);

        var result = await harness.Coordinator.CreateAsync(Manager, [PlayerA, PlayerB], Now);

        Assert.IsTrue(result.Accepted);
        Assert.AreEqual(8, result.Maps.Count);
        Assert.AreEqual(8, result.Maps.Select(map => map.MapId).Distinct(StringComparer.Ordinal).Count());
        CollectionAssert.AreEqual(
            new[] { "de_map10", "de_map09", "de_map08", "de_map07", "de_map06", "de_map05", "de_map04", "de_map03" },
            result.Maps.Select(map => map.MapId).ToArray());
    }

    [TestMethod]
    public async Task CreateAsync_RejectsCatalogsWithFewerThanEightMaps()
    {
        var harness = CreateHarness(7, minimumVotes: 1);

        var result = await harness.Coordinator.CreateAsync(Manager, [PlayerA], Now);

        Assert.IsFalse(result.Accepted);
        Assert.AreEqual(AnoVetoFailure.NotEnoughMaps, result.Failure);
    }

    [TestMethod]
    public async Task CompleteAsync_ChangesWinningMapExactlyOnce()
    {
        var harness = CreateHarness(8, minimumVotes: 1);
        var created = await harness.Coordinator.CreateAsync(Manager, [PlayerA, PlayerB], Now);
        var winner = created.Maps[0];

        Assert.IsTrue((await harness.Coordinator.CastAsync(PlayerA, winner.MapId, Now.AddSeconds(1))).Accepted);

        var result = await harness.Coordinator.CompleteAsync(Manager, Now.AddSeconds(3));
        var repeated = await harness.Coordinator.CompleteAsync(Manager, Now.AddSeconds(4));

        Assert.AreEqual(AnoVetoOutcome.MapSelected, result.Outcome);
        Assert.AreEqual(winner.MapId, result.Winner?.MapId);
        Assert.AreEqual(AnoVetoFailure.NotActive, repeated.Failure);
        Assert.AreEqual(1, harness.MapChanger.Changed.Count);
        Assert.AreEqual(winner.MapId, harness.MapChanger.Changed[0].MapId);
    }

    [TestMethod]
    public async Task ExpireAsync_ChangesMapAtMostOnce()
    {
        var harness = CreateHarness(8, minimumVotes: 1, duration: TimeSpan.FromSeconds(10));
        var created = await harness.Coordinator.CreateAsync(Manager, [PlayerA, PlayerB], Now);
        await harness.Coordinator.CastAsync(PlayerA, created.Maps[2].MapId, Now.AddSeconds(1));

        var first = await harness.Coordinator.ExpireAsync(Now.AddSeconds(10));
        var second = await harness.Coordinator.ExpireAsync(Now.AddSeconds(11));

        Assert.IsNotNull(first);
        Assert.IsNull(second);
        Assert.AreEqual(1, harness.MapChanger.Changed.Count);
    }

    [TestMethod]
    public async Task CancelAsync_NeverChangesMap()
    {
        var harness = CreateHarness(8, minimumVotes: 1);
        var created = await harness.Coordinator.CreateAsync(Manager, [PlayerA, PlayerB], Now);
        await harness.Coordinator.CastAsync(PlayerA, created.Maps[0].MapId, Now.AddSeconds(1));

        var result = await harness.Coordinator.CancelAsync(Manager, Now.AddSeconds(2));

        Assert.IsTrue(result.Accepted);
        Assert.AreEqual(AnoVetoOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(0, harness.MapChanger.Changed.Count);
    }

    [TestMethod]
    public async Task CompleteAsync_QuorumFailureDoesNotChangeMap()
    {
        var harness = CreateHarness(8, minimumVotes: 2);
        var created = await harness.Coordinator.CreateAsync(Manager, [PlayerA, PlayerB], Now);
        await harness.Coordinator.CastAsync(PlayerA, created.Maps[0].MapId, Now.AddSeconds(1));

        var result = await harness.Coordinator.CompleteAsync(Manager, Now.AddSeconds(2));

        Assert.AreEqual(AnoVetoOutcome.QuorumNotMet, result.Outcome);
        Assert.IsNull(result.Winner);
        Assert.AreEqual(0, harness.MapChanger.Changed.Count);
    }

    [TestMethod]
    public async Task CastAsync_SameSteamIdCannotVoteTwice()
    {
        var harness = CreateHarness(8, minimumVotes: 1);
        var created = await harness.Coordinator.CreateAsync(Manager, [PlayerA, PlayerB], Now);

        var first = await harness.Coordinator.CastAsync(PlayerA, created.Maps[0].MapId, Now.AddSeconds(1));
        var second = await harness.Coordinator.CastAsync(new PlayerId(PlayerA.SteamId64), created.Maps[1].MapId, Now.AddSeconds(2));

        Assert.IsTrue(first.Accepted);
        Assert.IsFalse(second.Accepted);
        Assert.AreEqual(AnoVetoFailure.AlreadyVoted, second.Failure);
    }

    private static Harness CreateHarness(int mapCount, int minimumVotes, TimeSpan? duration = null)
    {
        var maps = Enumerable.Range(1, mapCount)
            .Select(index => new MapDefinition($"Map {index:00}", $"de_map{index:00}"))
            .ToArray();
        var catalog = new MapCatalog(maps);
        var permissions = new AllowManagerPermissionEvaluator();
        var voteService = new VoteService(permissions);
        var changer = new RecordingMapChanger();
        var random = new ReverseRandomSource();
        var options = new AnoVetoOptions(duration ?? TimeSpan.FromSeconds(30), minimumVotes, VoteTieBreakPolicy.OptionOrder);
        var coordinator = new AnoVetoCoordinator(catalog, voteService, changer, random, options);
        return new Harness(coordinator, changer);
    }

    private sealed record Harness(AnoVetoCoordinator Coordinator, RecordingMapChanger MapChanger);

    private sealed class ReverseRandomSource : IAnoVetoRandomSource
    {
        public IReadOnlyList<T> Select<T>(IReadOnlyList<T> source, int count)
            => source.Reverse().Take(count).ToArray();
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

    private sealed class AllowManagerPermissionEvaluator : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(playerId == Manager);
    }
}
