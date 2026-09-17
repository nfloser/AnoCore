using AnoCore.Abstractions.Maps;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Modules.AnoVeto;
using AnoCore.Runtime.Maps;
using AnoCore.Runtime.Voting;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
public sealed class AnoVetoEligibilityTests
{
    private static readonly PlayerId Manager = new(76561198000000501);
    private static readonly PlayerId PlayerA = new(76561198000000502);

    [TestMethod]
    public async Task CreateAsync_MinimumVotesAboveEligiblePopulationRejectsCleanly()
    {
        var permissions = new AllowAllPermissions();
        var coordinator = new AnoVetoCoordinator(
            new MapCatalog(Enumerable.Range(1, 8)
                .Select(index => new MapDefinition($"Map {index:00}", $"de_map{index:00}"))),
            new VoteService(permissions),
            new NoOpMapChanger(),
            new StableRandomSource(),
            new AnoVetoOptions(TimeSpan.FromSeconds(30), minimumVotes: 2));

        var result = await coordinator.CreateAsync(
            Manager,
            [PlayerA],
            new DateTimeOffset(2026, 9, 17, 14, 0, 0, TimeSpan.Zero));

        Assert.IsFalse(result.Accepted);
        Assert.AreEqual(AnoVetoFailure.NotEnoughEligiblePlayers, result.Failure);
        Assert.IsFalse(coordinator.TryGetStatus(out _));
    }

    private sealed class AllowAllPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class NoOpMapChanger : IMapChanger
    {
        public ValueTask ChangeMapAsync(MapDefinition map, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class StableRandomSource : IAnoVetoRandomSource
    {
        public IReadOnlyList<T> Select<T>(IReadOnlyList<T> source, int count)
            => source.Take(count).ToArray();
    }
}
