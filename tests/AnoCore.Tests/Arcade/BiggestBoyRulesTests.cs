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
        Assert.AreEqual(10, bonusStops); // P(>=3 of five reels) = 0.00856.
    }

    [TestMethod]
    public void OnlyBestLeftToRightWinPaysAndScatterIsNeverWild()
    {
        var grid = Enumerable.Repeat(BiggestBoySymbol.Scatter, 25).ToArray();
        foreach (var cell in BiggestBoyRules.Paylines[0]) grid[cell] = BiggestBoySymbol.Beer;
        grid[5] = BiggestBoySymbol.Kebap;
        var result = BiggestBoyRules.EvaluateGrid(100, grid);
        Assert.AreEqual(1, result.LineWins.Length);
        Assert.AreEqual(BiggestBoySymbol.Beer, result.LineWins[0].Symbol);
        Assert.AreEqual(5, result.LineWins[0].MatchCount);
        Assert.AreEqual(BiggestBoyRules.Multiplier(BiggestBoySymbol.Beer, 5) * 10L, result.Payout);
        grid[0] = BiggestBoySymbol.Scatter;
        Assert.AreEqual(0, BiggestBoyRules.EvaluateGrid(100, grid).LineWins.Length);
    }

    [TestMethod]
    public void FreeSpinsKeepStakePreserveScattersAndStopAtThirty()
    {
        var trigger = BiggestBoyRules.StartRound(100, _ => 0);
        Assert.AreEqual(10, trigger.State.FreeSpinsRemaining);
        Assert.AreEqual(100, trigger.ChargedStake);
        var spin = trigger;
        while (spin.State.FreeSpinsRemaining > 0)
        {
            spin = BiggestBoyRules.PlayFreeSpin(spin.State, _ => 0);
            Assert.AreEqual(0, spin.ChargedStake);
            Assert.AreEqual(100, spin.State.LockedStake);
            Assert.AreEqual(5, spin.ScatterCount);
            Assert.IsTrue(spin.State.FreeSpinsAwarded <= 30);
        }
        Assert.AreEqual(31, spin.State.SpinsPlayed);
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyRules.PlayFreeSpin(spin.State, _ => 0));
    }

    [TestMethod]
    public void ExpansionOccursOnlyInBonusAndWinningCellsAreFromFinalGrid()
    {
        var paid = BiggestBoyRules.StartRound(100, _ => 1);
        var bonus = BiggestBoyRules.StartRound(100, _ => 0);
        var free = BiggestBoyRules.PlayFreeSpin(bonus.State, _ => 1);
        Assert.IsTrue(free.ExpandedReels.Length > 0);
        foreach (var reel in free.ExpandedReels)
            Assert.IsTrue(free.Grid.Skip(reel * 5).Take(5).All(s => s is BiggestBoySymbol.Kebap or BiggestBoySymbol.Scatter));
        Assert.AreEqual(0, paid.ExpandedReels.Length);
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyRules.DrawGrid(_ => 100));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BiggestBoyRules.EvaluateGrid(55, free.Grid));
    }
}
