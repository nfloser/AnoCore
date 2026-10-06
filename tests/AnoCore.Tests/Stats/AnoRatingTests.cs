using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class AnoRatingTests
{
    [TestMethod]
    public void EmptyAndSmallSamples_AreExplicitlyUnscored()
    {
        var empty = AnoRatingCalculator.Calculate(new CombatTotals(0, 0, 0), null, []);
        Assert.IsNull(empty.Score);
        Assert.AreEqual(AnoRatingConfidence.Provisional, empty.Confidence);
        var small = AnoRatingCalculator.Calculate(new CombatTotals(100, 1, 5), null,
            [new GameplayStatTotal(GameplayStatKind.RoundPlayed, 19)]);
        Assert.IsNull(small.Score);
        Assert.AreEqual(19L, small.Rounds);
    }

    [TestMethod]
    public void FixedFixture_IsDeterministicAndShowsDimensions()
    {
        var gameplay = new GameplayStatTotal[]
        {
            new(GameplayStatKind.RoundPlayed, 100),
            new(GameplayStatKind.RoundWon, 60),
            new(GameplayStatKind.RoundLost, 40),
            new(GameplayStatKind.BombPlanted, 10),
            new(GameplayStatKind.FlashAssist, 10),
        };
        var result = AnoRatingCalculator.Calculate(new CombatTotals(100, 100, 0),
            new CombatDetailTotals(1000, 400, 10000, 0, 100), gameplay);
        Assert.AreEqual(560, result.Score);
        Assert.AreEqual(AnoRatingConfidence.Medium, result.Confidence);
        Assert.AreEqual(5, result.Dimensions.Count);
        Assert.AreEqual(AnoRatingCalculator.Version, result.AlgorithmVersion);
        Assert.AreEqual(560, AnoRatingCalculator.Calculate(new CombatTotals(100, 100, 0),
            new CombatDetailTotals(1000, 400, 10000, 0, 100), gameplay.Reverse().ToArray()).Score);
    }

    [TestMethod]
    public void Confidence_RequiresSamplesAndCoverage()
    {
        static AnoRatingResult At(long rounds, CombatDetailTotals? detail = null)
            => AnoRatingCalculator.Calculate(new CombatTotals(rounds, rounds, 0), detail,
                [new(GameplayStatKind.RoundPlayed, rounds),
                    new(GameplayStatKind.RoundWon, rounds / 2),
                    new(GameplayStatKind.RoundLost, rounds / 2)]);
        Assert.AreEqual(AnoRatingConfidence.Low, At(20).Confidence);
        Assert.AreEqual(AnoRatingConfidence.Low, At(99).Confidence);
        Assert.AreEqual(AnoRatingConfidence.Medium, At(100).Confidence);
        Assert.AreEqual(AnoRatingConfidence.Medium, At(500).Confidence);
        Assert.AreEqual(AnoRatingConfidence.High,
            At(500, new CombatDetailTotals(500, 200, 40000, 0, 50)).Confidence);
        Assert.AreEqual(AnoRatingConfidence.Medium,
            At(499, new CombatDetailTotals(500, 200, 40000, 0, 50)).Confidence);
    }

    [TestMethod]
    public void MissingDimensions_AreOmittedAndNegativeCountsRejected()
    {
        var result = AnoRatingCalculator.Calculate(new CombatTotals(20, 20, 0), null,
            [new(GameplayStatKind.RoundPlayed, 20)]);
        Assert.IsFalse(result.Dimensions.Any(dimension => dimension.Name == "impact"));
        Assert.IsFalse(result.Dimensions.Any(dimension => dimension.Name == "consistency"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AnoRatingCalculator.Calculate(new CombatTotals(-1, 0, 0), null, []));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AnoRatingCalculator.Calculate(new CombatTotals(0, 0, 0), null,
                [new(GameplayStatKind.RoundPlayed, -1)]));
    }
}
