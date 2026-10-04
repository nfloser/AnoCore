using AnoCore.Abstractions.Maps;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Modules.AnoVeto;
using AnoCore.Modules.Tournament;
using AnoCore.Runtime.Maps;
using AnoCore.Runtime.Voting;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
public sealed class AnoVetoTournamentMapSelectionTests
{
    private static readonly PlayerId Manager = new(76561198000205001);

    [TestMethod]
    public async Task AutoFinalizedVote_ResolvesWinnerFirstAndUniqueBestOfSeries()
    {
        var catalog = new MapCatalog(Enumerable.Range(1, 8)
            .Select(i => new MapDefinition($"Map {i}", $"de_map{i}"))
            .ToArray());
        var votes = new VoteService(new AllowAll());
        var coordinator = new AnoVetoCoordinator(
            catalog, votes, new RecordingMapChanger(), new StableRandom(),
            new AnoVetoOptions(
                TimeSpan.FromSeconds(60),
                minimumVotes: 1,
                VoteTieBreakPolicy.OptionOrder));
        var source = new AnoVetoTournamentMapSelectionSource(
            coordinator, new FixedTime(new DateTimeOffset(2026, 10, 4, 19, 0, 0, TimeSpan.Zero)));
        var request = new TournamentMapSelectionRequest(
            Guid.NewGuid(), 7, TournamentBestOf.Three, Manager, [Manager]);

        await source.StartAsync(request);
        var cast = await coordinator.CastAsync(
            Manager, "de_map2", new DateTimeOffset(2026, 10, 4, 19, 0, 1, TimeSpan.Zero));
        Assert.AreEqual(AnoVetoOutcome.MapSelected, cast.Outcome);

        var result = await source.CompleteAsync(Manager);

        Assert.IsNotNull(result);
        Assert.AreEqual(request.MatchId, result.MatchId);
        Assert.AreEqual(7, result.Revision);
        Assert.HasCount(3, result.Maps);
        Assert.AreEqual("de_map2", result.Maps[0]);
        Assert.AreEqual(3, result.Maps.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private sealed class AllowAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId id, AnoCore.Abstractions.Permissions.PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class RecordingMapChanger : IMapChanger
    {
        public ValueTask ChangeMapAsync(
            MapDefinition map, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class StableRandom : IAnoVetoRandomSource
    {
        public IReadOnlyList<T> Select<T>(IReadOnlyList<T> source, int count)
            => source.Take(count).ToArray();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
