using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class BiggestBoyRulesTests
{
    [TestMethod]
    public void EveryStopIsAContiguousFiveCellWindowAndScatterIsRare()
    {
        var bonusStops = 0;
        for (var stop = 0; stop < 100; stop++)
        {
            var grid = BiggestBoyRules.DrawGrid(_ => stop);
            Assert.AreEqual(25, grid.Length);
            for (var reel = 0; reel < 5; reel++)
            {
                var scatters = 0;
                for (var row = 0; row < 5; row++)
                {
                    Assert.AreEqual(BiggestBoyRules.ReelStrips[reel][(stop + row) % 100], grid[reel * 5 + row]);
                    if (grid[reel * 5 + row] == BiggestBoySymbol.Scatter) scatters++;
                }
                Assert.IsTrue(scatters <= 1);
                if (reel == 0 && scatters == 1) bonusStops++;
            }
        }
        Assert.AreEqual(10, bonusStops);
    }

    [TestMethod]
    public void TwentyFiveLinesPayOnlyBestLeftToRightBaseWinAndScatterIsNeverWild()
    {
        Assert.AreEqual(25, BiggestBoyRules.PaylineCount);
        Assert.AreEqual(25, BiggestBoyRules.Paylines.Count);
        Assert.AreEqual(25, BiggestBoyRules.Paylines.Distinct(new LineComparer()).Count());

        var grid = Enumerable.Repeat(BiggestBoySymbol.Scatter, 25).ToArray();
        foreach (var cell in BiggestBoyRules.Paylines[0]) grid[cell] = BiggestBoySymbol.Beer;
        grid[5] = BiggestBoySymbol.Kebap;
        var result = BiggestBoyRules.EvaluateGrid(100, grid);
        Assert.AreEqual(1, result.LineWins.Length);
        Assert.AreEqual(BiggestBoySymbol.Beer, result.LineWins[0].Symbol);
        Assert.AreEqual(5, result.LineWins[0].MatchCount);
        Assert.AreEqual(BiggestBoyRules.Multiplier(BiggestBoySymbol.Beer, 5) * 4L, result.Payout);
        grid[0] = BiggestBoySymbol.Scatter;
        Assert.AreEqual(0, BiggestBoyRules.EvaluateGrid(100, grid).LineWins.Length);
    }

    [TestMethod]
    public void ScatterCountAwardsTenFifteenOrTwentyFreeSpins()
    {
        Assert.AreEqual(10, BiggestBoyRules.FreeSpinsForScatters(3));
        Assert.AreEqual(15, BiggestBoyRules.FreeSpinsForScatters(4));
        Assert.AreEqual(20, BiggestBoyRules.FreeSpinsForScatters(5));
        Assert.AreEqual(0, BiggestBoyRules.FreeSpinsForScatters(2));

        static BiggestBoySpinResult StartWith(params int[] stops)
        {
            var queue = new Queue<int>(stops);
            return BiggestBoyRules.StartRound(100, _ => queue.Dequeue());
        }

        Assert.AreEqual(10, StartWith(0, 0, 0, 20, 20).State.FreeSpinsRemaining);
        Assert.AreEqual(15, StartWith(0, 0, 0, 0, 20).State.FreeSpinsRemaining);
        Assert.AreEqual(20, StartWith(0, 0, 0, 0, 0).State.FreeSpinsRemaining);
    }

    [TestMethod]
    public void BonusRequiresOneSelectableSymbolAndRetriggersStopAtThirty()
    {
        var trigger = BiggestBoyRules.StartRound(100, _ => 0);
        Assert.AreEqual(20, trigger.State.FreeSpinsRemaining);
        Assert.IsNull(trigger.State.ExpandingSymbol);
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyRules.PlayFreeSpin(trigger.State, _ => 0));
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyRules.SelectExpandingSymbol(trigger, BiggestBoySymbol.Kebap));
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyRules.SelectExpandingSymbol(trigger, BiggestBoySymbol.Scatter));

        var selected = BiggestBoyRules.SelectExpandingSymbol(trigger, BiggestBoySymbol.Beer);
        Assert.AreEqual(0, selected.ChargedStake);
        Assert.AreEqual(BiggestBoySymbol.Beer, selected.State.ExpandingSymbol);
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyRules.SelectExpandingSymbol(selected, BiggestBoySymbol.Butter));

        var spin = selected;
        while (spin.State.FreeSpinsRemaining > 0)
        {
            spin = BiggestBoyRules.PlayFreeSpin(spin.State, _ => 0);
            Assert.AreEqual(0, spin.ChargedStake);
            Assert.AreEqual(100, spin.State.LockedStake);
            Assert.AreEqual(BiggestBoySymbol.Beer, spin.State.ExpandingSymbol);
            Assert.AreEqual(5, spin.ScatterCount);
            Assert.IsTrue(spin.State.FreeSpinsAwarded <= 30);
        }
        Assert.AreEqual(31, spin.State.SpinsPlayed);
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyRules.PlayFreeSpin(spin.State, _ => 0));
    }

    [TestMethod]
    public void ExpansionPreservesRawGridAndScatterAndUsesSelectedSymbolNotWild()
    {
        var trigger = BiggestBoyRules.SelectExpandingSymbol(
            BiggestBoyRules.StartRound(100, _ => 0), BiggestBoySymbol.Beer);
        var stop = Enumerable.Range(0, 100)
            .First(value => BiggestBoyRules.DrawGrid(_ => value).Contains(BiggestBoySymbol.Beer));
        var free = BiggestBoyRules.PlayFreeSpin(trigger.State, _ => stop);

        Assert.AreEqual(25, free.RawGrid.Length);
        Assert.IsTrue(free.ExpandedReels.Length > 0);
        foreach (var reel in free.ExpandedReels)
        {
            Assert.IsTrue(free.RawGrid.Skip(reel * 5).Take(5).Contains(BiggestBoySymbol.Beer));
            Assert.IsTrue(free.Grid.Skip(reel * 5).Take(5)
                .All(symbol => symbol is BiggestBoySymbol.Beer or BiggestBoySymbol.Scatter));
        }
        Assert.AreEqual(
            free.RawGrid.Count(symbol => symbol == BiggestBoySymbol.Scatter),
            free.Grid.Count(symbol => symbol == BiggestBoySymbol.Scatter));
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyRules.DrawGrid(_ => 100));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BiggestBoyRules.EvaluateGrid(55, free.Grid));
    }

    [TestMethod]
    public void StakeButtonsUseApprovedStepsOnly()
    {
        CollectionAssert.AreEqual(new[] { 100, 250, 500, 1000, 2500, 5000, 10000 }, BiggestBoyRules.StakeSteps.ToArray());
        Assert.AreEqual(250, BiggestBoyRules.StepStake(100, 1, 10_000));
        Assert.AreEqual(1000, BiggestBoyRules.StepStake(2500, -1, 10_000));
        Assert.AreEqual(500, BiggestBoyRules.ClampStakeToConfiguredMaximum(10_000, 750));
        Assert.IsFalse(BiggestBoyRules.IsValidStake(50));
        Assert.IsFalse(BiggestBoyRules.IsValidStake(1050));
    }

    private sealed class LineComparer : IEqualityComparer<IReadOnlyList<int>>
    {
        public bool Equals(IReadOnlyList<int>? x, IReadOnlyList<int>? y) =>
            x is not null && y is not null && x.SequenceEqual(y);
        public int GetHashCode(IReadOnlyList<int> obj) =>
            obj.Aggregate(17, (hash, value) => hash * 31 + value);
    }
}
