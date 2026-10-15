using System.Collections.Immutable;
using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class BiggestBoyExpandingRulesTests
{
    [TestMethod]
    public void EveryWindowHasFifteenCellsAndUsesExactlyTheBookPaylines()
    {
        Assert.AreEqual(10, BiggestBoyExpandingRules.Paylines.Count);
        for (var line = 0; line < 10; line++) CollectionAssert.AreEqual(BonusSlotRules.Paylines[line].ToArray(), BiggestBoyExpandingRules.Paylines[line].ToArray());
        for (var stop = 0; stop < 100; stop++)
        {
            var spin = BiggestBoyExpandingRules.StartRound(100, bound => bound == 100 ? stop : 0);
            Assert.AreEqual(15, spin.Grid.Length);
            for (var reel = 0; reel < 5; reel++)
                for (var row = 0; row < 3; row++)
                    Assert.AreEqual(BiggestBoyExpandingRules.ReelStrips[reel][(spin.Stops[reel] + row) % 100], spin.Grid[reel * 3 + row]);
        }
        Assert.AreEqual(12, BiggestBoyExpandingRules.ReelStrips.SelectMany(x => x).Distinct().Count());
    }

    [TestMethod]
    public void BookIsWildAndKebapIsTheTopRegularSymbol()
    {
        var grid = Enumerable.Repeat(BiggestBoySymbol.Scatter, 15).ToArray();
        foreach (var cell in BiggestBoyExpandingRules.Paylines[0]) grid[cell] = BiggestBoySymbol.Kebap;
        grid[6] = BiggestBoySymbol.Book;
        var spin = BiggestBoyExpandingRules.EvaluateGrid(100, grid);
        Assert.AreEqual(1, spin.LineWins.Length);
        Assert.AreEqual(BiggestBoySymbol.Kebap, spin.LineWins[0].Symbol);
        Assert.AreEqual(10L * BiggestBoyExpandingRules.Multiplier(BiggestBoySymbol.Kebap, 5), spin.Payout);
        grid[0] = BiggestBoySymbol.Beer;
        Assert.AreEqual(0L, BiggestBoyExpandingRules.EvaluateGrid(100, grid).Payout);
        var allWild = BiggestBoyExpandingRules.EvaluateGrid(100, Enumerable.Repeat(BiggestBoySymbol.Book, 15).ToArray());
        Assert.AreEqual(10, allWild.LineWins.Length);
        Assert.AreEqual(100L * BiggestBoyExpandingRules.Multiplier(BiggestBoySymbol.Kebap, 5), allWild.Payout);
    }

    [TestMethod]
    public void ScatterAwardsAndExpandingSelectionAreFrozenAtBonusStart()
    {
        for (var count = 3; count <= 5; count++)
        {
            var queue = new Queue<int>(Enumerable.Range(0, 5).Select(i => i < count ? 0 : 30).Append(9));
            var spin = BiggestBoyExpandingRules.StartRound(100, bound => queue.Dequeue());
            Assert.AreEqual(count == 3 ? 10 : count == 4 ? 15 : 20, spin.State.FreeSpinsRemaining);
            Assert.AreEqual(BiggestBoySymbol.T, spin.State.ExpandingSymbol);
            Assert.AreEqual(BiggestBoyExpandingRules.Version, spin.State.RulesVersion);
            Assert.AreEqual(100, spin.ChargedStake);
        }
    }

    [TestMethod]
    public void ExpansionFillsExactlyThreeCellsAndPaysTheExpandedGridOnce()
    {
        var trigger = BiggestBoyExpandingRules.StartRound(100, _ => 0);
        var chosen = trigger.State.ExpandingSymbol!.Value;
        var stops = Enumerable.Range(0, 5).Select(reel => Enumerable.Range(0, 100).First(stop =>
            Enumerable.Range(0, 3).Any(row => BiggestBoyExpandingRules.ReelStrips[reel][(stop + row) % 100] == chosen))).ToArray();
        var queue = new Queue<int>(stops);
        var spin = BiggestBoyExpandingRules.PlayFreeSpin(trigger.State, _ => queue.Dequeue());
        Assert.AreEqual(5, spin.ExpandedReels.Length);
        Assert.IsTrue(spin.Grid.All(symbol => symbol == chosen));
        Assert.AreEqual(15, spin.RawGrid.Length);
        Assert.AreEqual(BiggestBoyExpandingRules.EvaluateGrid(100, spin.Grid).Payout, spin.Payout);
        Assert.AreEqual(spin.LineWins.Sum(win => win.Payout), spin.Payout);
        Assert.AreEqual(trigger.State.TotalPayout + spin.Payout, spin.State.TotalPayout);
        Assert.AreEqual(0, spin.ChargedStake);
        Assert.AreEqual(chosen, spin.State.ExpandingSymbol);
    }

    [TestMethod]
    public void RetriggersUseRawScattersAndNeverPlayMoreThanThirtyFreeSpins()
    {
        var spin = BiggestBoyExpandingRules.StartRound(100, _ => 0);
        var played = 0;
        while (spin.State.FreeSpinsRemaining > 0)
        {
            spin = BiggestBoyExpandingRules.PlayFreeSpin(spin.State, _ => 0);
            Assert.AreEqual(5, spin.ScatterCount);
            Assert.AreEqual(5, spin.RawGrid.Count(x => x == BiggestBoySymbol.Scatter));
            Assert.IsTrue(spin.State.FreeSpinsAwarded <= 30);
            Assert.AreEqual(0, spin.ChargedStake);
            played++;
            Assert.IsTrue(played <= 30);
        }
        Assert.AreEqual(30, played);
        Assert.AreEqual(31, spin.State.SpinsPlayed);
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyExpandingRules.PlayFreeSpin(spin.State, _ => 0));
    }

    [TestMethod]
    public void ScatterCoveredByExpansionStillRetriggersAndFreeSeriesExcludesPaidSpin()
    {
        var selected = BiggestBoyExpandingRules.ReelStrips[0][1];
        var queue = new Queue<int>(Enumerable.Repeat(0, 5).Append((int)selected));
        var trigger = BiggestBoyExpandingRules.StartRound(100, _ => queue.Dequeue());
        var state = trigger.State with { TotalPayout = trigger.State.TotalPayout + 500 };
        var spin = BiggestBoyExpandingRules.PlayFreeSpin(state, _ => 0);
        Assert.AreEqual(5, spin.ScatterCount);
        Assert.IsTrue(spin.Grid.Count(x => x == BiggestBoySymbol.Scatter) < spin.ScatterCount);
        Assert.AreEqual(25, spin.State.FreeSpinsAwarded);
        Assert.AreEqual(24, spin.State.FreeSpinsRemaining);
        Assert.AreEqual(spin.Payout, spin.State.BonusPayout);
        Assert.AreEqual(state.TotalPayout + spin.Payout, spin.State.TotalPayout);
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public void ThreeFourAndFiveMatchingSymbolsPayTheFrozenLineFactor(int matches)
    {
        var grid = Enumerable.Repeat(BiggestBoySymbol.Scatter, 15).ToArray();
        foreach (var cell in BiggestBoyExpandingRules.Paylines[0].Take(matches)) grid[cell] = BiggestBoySymbol.Beer;
        grid[3] = BiggestBoySymbol.Book;
        var spin = BiggestBoyExpandingRules.EvaluateGrid(250, grid);
        Assert.AreEqual(1, spin.LineWins.Length);
        Assert.AreEqual(matches, spin.LineWins[0].MatchCount);
        Assert.AreEqual(25L * BiggestBoyExpandingRules.Multiplier(BiggestBoySymbol.Beer, matches), spin.Payout);
    }

    [TestMethod]
    public void InvalidStateAndRandomSourceCannotChangeAnAcceptedRound()
    {
        var trigger = BiggestBoyExpandingRules.StartRound(100, _ => 0);
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyExpandingRules.StartRound(100, bound => bound));
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyExpandingRules.PlayFreeSpin(trigger.State with { ExpandingSymbol = BiggestBoySymbol.Book }, _ => 0));
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyExpandingRules.PlayFreeSpin(trigger.State with { RulesVersion = BiggestBoyCollectorRules.Version }, _ => 0));
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyExpandingRules.EvaluateGrid(100, ImmutableArray<BiggestBoySymbol>.Empty));
    }
}
