using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class BiggestBoyCollectorRulesTests
{
    [TestMethod]
    public void ThreeRowsAndTenUniqueLinesUseOnlyAdjacentBookedCells()
    {
        Assert.AreEqual(10, BiggestBoyCollectorRules.Paylines.Count);
        Assert.AreEqual(10, BiggestBoyCollectorRules.Paylines.Select(line => string.Join(",", line)).Distinct().Count());
        for (var stop = 0; stop < 100; stop++)
        {
            var spin = BiggestBoyCollectorRules.StartRound(100, _ => stop);
            Assert.AreEqual(15, spin.Grid.Length);
            Assert.AreEqual(BiggestBoyCollectorRules.Version, spin.State.RulesVersion);
            for (var reel = 0; reel < 5; reel++)
                for (var row = 0; row < 3; row++)
                    Assert.AreEqual(BiggestBoyRules.ReelStrips[reel][(stop + row) % 100], spin.Grid[reel * 3 + row]);
        }
        foreach (var line in BiggestBoyCollectorRules.Paylines)
            for (var reel = 0; reel < 5; reel++) Assert.AreEqual(reel, line[reel] / 3);
        var grid = Enumerable.Repeat(BiggestBoySymbol.Scatter, 15).ToArray();
        foreach (var cell in BiggestBoyCollectorRules.Paylines[0]) grid[cell] = BiggestBoySymbol.Beer;
        grid[3] = BiggestBoySymbol.Kebap;
        var result = BiggestBoyCollectorRules.EvaluateGrid(100, grid);
        Assert.AreEqual(1, result.LineWins.Length);
        Assert.AreEqual(10L * BiggestBoyCollectorRules.LineMultiplier(BiggestBoySymbol.Beer, 5), result.Payout);
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyCollectorRules.EvaluateGrid(100, grid[..14]));
    }

    [TestMethod]
    public void EveryCollectorPaysEveryMoneyValueAndNextStageStartsNextSpin()
    {
        var state = BonusState(collectors: 3);
        var spin = BiggestBoyCollectorRules.PlayFreeSpin(state, DrawWithWildsAndBeer(2));
        Assert.AreEqual(2, spin.Grid.Count(symbol => symbol == BiggestBoySymbol.Kebap));
        Assert.IsTrue(spin.MoneyValues.Sum() > 0);
        Assert.AreEqual(spin.MoneyValues.Sum() * 2, spin.CollectionPayout);
        Assert.AreEqual(1, spin.CollectionMultiplier);
        Assert.AreEqual(5, spin.State.Collectors);
        Assert.AreEqual(state.FreeSpinsRemaining + 9, spin.State.FreeSpinsRemaining);
        Assert.AreEqual(2, BiggestBoyCollectorRules.MultiplierForCollectors(spin.State.Collectors));
        Assert.AreEqual(spin.LineWins.Sum(win => win.Payout) + spin.CollectionPayout, spin.Payout);
        Assert.AreEqual(0, spin.ChargedStake);
        Assert.AreEqual(state.TotalPayout + spin.Payout, spin.State.TotalPayout);
    }

    [TestMethod]
    public void LastFreeSpinCanRetriggerAndMultipleThresholdsAreNotLost()
    {
        var state = BonusState(collectors: 3) with { FreeSpinsRemaining = 1, SpinsPlayed = 10 };
        var spin = BiggestBoyCollectorRules.PlayFreeSpin(state, DrawWithWildsAndBeer(5));
        Assert.AreEqual(8, spin.State.Collectors);
        Assert.AreEqual(20, spin.State.FreeSpinsRemaining);
        Assert.AreEqual(30, spin.State.FreeSpinsAwarded);
        Assert.AreEqual(0L, spin.CollectionPayout);
        Assert.AreEqual(3, BiggestBoyCollectorRules.MultiplierForCollectors(8));
    }

    [TestMethod]
    public void FinalStageDoesNotRetriggerAgainAndScattersDoNotExtendBonus()
    {
        var state = BonusState(collectors: 12) with { FreeSpinsAwarded = 40, FreeSpinsRemaining = 1, SpinsPlayed = 40 };
        var spin = BiggestBoyCollectorRules.PlayFreeSpin(state, DrawWithWildsAndBeer(2));
        Assert.AreEqual(10, spin.CollectionMultiplier);
        Assert.AreEqual(0, spin.State.FreeSpinsRemaining);
        Assert.AreEqual(40, spin.State.FreeSpinsAwarded);
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyCollectorRules.PlayFreeSpin(spin.State, _ => 0));
        var scatterSpin = BiggestBoyCollectorRules.PlayFreeSpin(BonusState(), _ => 0);
        Assert.AreEqual(5, scatterSpin.ScatterCount);
        Assert.AreEqual(9, scatterSpin.State.FreeSpinsRemaining);
    }

    [TestMethod]
    public void MoneyWithoutWildDoesNotPayCollectionAndDrawsAreValidated()
    {
        var spin = BiggestBoyCollectorRules.PlayFreeSpin(BonusState(), DrawWithWildsAndBeer(0));
        Assert.IsTrue(spin.MoneyValues.Sum() > 0);
        Assert.AreEqual(0L, spin.CollectionPayout);
        Assert.AreEqual(0, spin.State.Collectors);
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyCollectorRules.StartRound(100, _ => 100));
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyCollectorRules.PlayFreeSpin(BonusState() with { RulesVersion = BiggestBoyRules.Version }, _ => 0));
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyCollectorRules.PlayFreeSpin(BonusState() with { Collectors = 13 }, _ => 0));
    }

    [TestMethod]
    public void PublishedRoundCeilingSettlesImmediatelyWithoutOverpaying()
    {
        var state = BonusState() with { TotalPayout = BiggestBoyCollectorRules.MaximumRoundExposure(100) - 1 };
        var spin = BiggestBoyCollectorRules.PlayFreeSpin(state, DrawWithWildsAndBeer(2));
        Assert.AreEqual(1L, spin.Payout);
        Assert.AreEqual(BiggestBoyCollectorRules.MaximumRoundExposure(100), spin.State.TotalPayout);
        Assert.AreEqual(0, spin.State.FreeSpinsRemaining);
    }

    private static BiggestBoyRoundState BonusState(int collectors = 0) =>
        new(100, 10, 10 + collectors / 4 * 10, 1 + collectors / 4 * 10, 0, BiggestBoyCollectorRules.Version, null, collectors);

    private static Func<int, int> DrawWithWildsAndBeer(int wilds)
    {
        var stops = Enumerable.Range(0, 5).Select(reel => reel < wilds ? 5 : Enumerable.Range(0, 100).First(stop =>
            Enumerable.Range(0, 3).Any(row => BiggestBoyRules.ReelStrips[reel][(stop + row) % 100] == BiggestBoySymbol.Beer)
            && !Enumerable.Range(0, 3).Any(row => BiggestBoyRules.ReelStrips[reel][(stop + row) % 100] == BiggestBoySymbol.Kebap))).ToArray();
        var queue = new Queue<int>(stops);
        return _ => queue.Count > 0 ? queue.Dequeue() : 0;
    }
}
