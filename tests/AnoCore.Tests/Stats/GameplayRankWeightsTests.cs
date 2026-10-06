using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class GameplayRankWeightsTests
{
    [TestMethod]
    public void Configuration_RejectsUnknownKindsAndUnboundedWeights()
    {
        var configuration = new RankConfiguration
        {
            GameplayPoints = new() { [(GameplayStatKind)255] = 1 },
        };
        Assert.IsNotEmpty(RankConfiguration.Validate(configuration));
        configuration.GameplayPoints = new() { [GameplayStatKind.Mvp] = 1001 };
        Assert.IsNotEmpty(RankConfiguration.Validate(configuration));
        configuration.GameplayPoints = new() { [GameplayStatKind.HostageKilled] = -1000 };
        Assert.IsEmpty(RankConfiguration.Validate(configuration));
        configuration.StartingPoints = -1;
        Assert.IsNotEmpty(RankConfiguration.Validate(configuration));
        configuration.StartingPoints = RankScoreWeights.MaximumStartingPoints + 1;
        Assert.IsNotEmpty(RankConfiguration.Validate(configuration));
        configuration.StartingPoints = RankScoreWeights.MaximumStartingPoints;
        Assert.IsEmpty(RankConfiguration.Validate(configuration));
        configuration.ScoringMode = (RankScoringMode)255;
        Assert.IsNotEmpty(RankConfiguration.Validate(configuration));
        configuration.ScoringMode = RankScoringMode.EventLedger;
        Assert.IsEmpty(RankConfiguration.Validate(configuration));
    }

    [TestMethod]
    public void Weights_SnapshotCallerDictionaryAndDiscardZeroWeights()
    {
        var source = new Dictionary<GameplayStatKind, int>
        {
            [GameplayStatKind.Mvp] = 5,
            [GameplayStatKind.RoundPlayed] = 0,
        };
        var weights = new RankScoreWeights(2, 1, 1, source);
        source[GameplayStatKind.Mvp] = 999;
        Assert.AreEqual(5, weights.GameplayPoints[GameplayStatKind.Mvp]);
        Assert.AreEqual(1, weights.GameplayPoints.Count);
        Assert.AreEqual(0L, weights.StartingPoints);
        Assert.AreEqual(RankScoringMode.Derived, weights.ScoringMode);
        var ledgerWeights = new RankScoreWeights(
            2, 1, 1, 25, source, RankScoringMode.EventLedger);
        Assert.AreEqual(25L, ledgerWeights.StartingPoints);
        Assert.AreEqual(RankScoringMode.EventLedger, ledgerWeights.ScoringMode);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new RankScoreWeights(2, 1, 1, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new RankScoreWeights(2, 1, 1, RankScoreWeights.MaximumStartingPoints + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new RankScoreWeights(2, 1, 1,
                new Dictionary<GameplayStatKind, int> { [GameplayStatKind.Mvp] = -1001 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new RankScoreWeights(2, 1, 1, 0, scoringMode: (RankScoringMode)255));
    }
}
