using AnoCore.Abstractions.Maps;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Modules.AnoVeto;
using AnoCore.Runtime.Maps;
using AnoCore.Runtime.Voting;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
public sealed class AnoVetoLifecycleTests
{
    private static readonly PlayerId Manager = new(76561198000000201);
    private static readonly PlayerId PlayerA = new(76561198000000202);
    private static readonly PlayerId PlayerB = new(76561198000000203);
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CastAsync_FinalEligibleBallotFinalizesAndChangesMapImmediately()
    {
        var harness = CreateHarness(TimeSpan.FromMinutes(1));
        var created = await harness.Coordinator.CreateAsync(Manager, [PlayerA, PlayerB], Now);
        var winner = created.Maps[0];

        var first = await harness.Coordinator.CastAsync(PlayerA, winner.MapId, Now.AddSeconds(1));
        var last = await harness.Coordinator.CastAsync(PlayerB, winner.MapId, Now.AddSeconds(2));

        Assert.IsTrue(first.Accepted);
        Assert.IsTrue(last.Accepted);
        Assert.AreEqual(AnoVetoOutcome.MapSelected, last.Outcome);
        Assert.AreEqual(winner.MapId, last.Winner?.MapId);
        Assert.IsFalse(harness.Coordinator.TryGetStatus(out _));
        Assert.HasCount(1, harness.MapChanger.Changed);
        Assert.AreEqual(winner.MapId, harness.MapChanger.Changed[0].MapId);
    }

    [TestMethod]
    public async Task ExpireAsync_StillFinalizesAfterCastArrivesAtDeadline()
    {
        var harness = CreateHarness(TimeSpan.FromSeconds(10));
        var created = await harness.Coordinator.CreateAsync(Manager, [PlayerA, PlayerB], Now);
        var winner = created.Maps[1];
        Assert.IsTrue((await harness.Coordinator.CastAsync(PlayerA, winner.MapId, Now.AddSeconds(1))).Accepted);

        var late = await harness.Coordinator.CastAsync(PlayerB, created.Maps[2].MapId, Now.AddSeconds(10));
        var expired = await harness.Coordinator.ExpireAsync(Now.AddSeconds(10));

        Assert.IsFalse(late.Accepted);
        Assert.AreEqual(AnoVetoFailure.NotActive, late.Failure);
        Assert.IsNotNull(expired);
        Assert.AreEqual(AnoVetoOutcome.MapSelected, expired.Outcome);
        Assert.AreEqual(winner.MapId, expired.Winner?.MapId);
        Assert.IsFalse(harness.Coordinator.TryGetStatus(out _));
        Assert.HasCount(1, harness.MapChanger.Changed);
    }

    private static Harness CreateHarness(TimeSpan duration)
    {
        var catalog = new MapCatalog(Enumerable.Range(1, 8)
            .Select(index => new MapDefinition($"Map {index:00}", $"de_map{index:00}")));
        var permissions = new ManagerPermissionEvaluator();
        var changer = new RecordingMapChanger();
        var coordinator = new AnoVetoCoordinator(
            catalog,
            new VoteService(permissions),
            changer,
            new StableRandomSource(),
            new AnoVetoOptions(duration, 1, VoteTieBreakPolicy.OptionOrder));
        return new Harness(coordinator, changer);
    }

    private sealed record Harness(AnoVetoCoordinator Coordinator, RecordingMapChanger MapChanger);

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

    private sealed class ManagerPermissionEvaluator : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(playerId == Manager);
    }
}
