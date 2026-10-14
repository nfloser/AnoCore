using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class BonusSlotRulesTests
{
    [TestMethod]
    public void GridUsesFiveReelsAndThreeRowsAndHasTenFixedLines()
    {
        Assert.AreEqual(15, BonusSlotRules.GridSize);
        Assert.AreEqual(10, BonusSlotRules.Paylines.Count);
        Assert.AreEqual(0L, BonusSlotRules.EvaluateGrid(100, Enumerable.Repeat(BonusSlotSymbol.Cherry, 15).ToArray()).Payout);
    }

    [TestMethod]
    public void ThreeBookScattersTriggerTenSpinsAndLockPaidStake()
    {
        var sequence = Grid(BonusSlotSymbol.Book, BonusSlotSymbol.Book, BonusSlotSymbol.Book);
        var result = BonusSlotRules.StartRound(100, sequence.Next);
        Assert.AreEqual(3, result.ScatterCount);
        Assert.AreEqual(10, result.State.FreeSpinsRemaining);
        Assert.AreEqual(10, result.State.FreeSpinsAwarded);
        Assert.AreEqual(100, result.State.LockedStake);
        Assert.AreEqual(0L, result.State.TotalPayout);
        Assert.AreEqual(0, result.State.ChargedStake);
    }

    [TestMethod]
    public void BooksAreWildForLineWinsAndScatterPayIsIndependent()
    {
        var grid = Enumerable.Repeat(BonusSlotSymbol.Lemon, 15).ToArray();
        grid[0] = BonusSlotSymbol.Book;
        grid[3] = BonusSlotSymbol.Book;
        grid[6] = BonusSlotSymbol.Book;
        var outcome = BonusSlotRules.EvaluateGrid(100, grid);
        Assert.IsTrue(outcome.LineWins.Any(win => win.Symbol == BonusSlotSymbol.Lemon && win.MatchCount >= 3 && win.Payout > 0));
        Assert.AreEqual(3, outcome.ScatterCount);
    }

    [TestMethod]
    public void ExpandingSymbolExpandsBeforeFreeSpinLineEvaluation()
    {
        var trigger = Grid(BonusSlotSymbol.Book, BonusSlotSymbol.Book, BonusSlotSymbol.Book);
        var started = BonusSlotRules.StartRound(100, trigger.Next);
        var expanding = started.State.ExpandingSymbol!.Value;
        var freeGrid = new[] { expanding, BonusSlotSymbol.Cherry, BonusSlotSymbol.Cherry,
            BonusSlotSymbol.Lemon, BonusSlotSymbol.Cherry, BonusSlotSymbol.Cherry,
            BonusSlotSymbol.Plum, BonusSlotSymbol.Cherry, BonusSlotSymbol.Cherry,
            BonusSlotSymbol.Orange, BonusSlotSymbol.Cherry, BonusSlotSymbol.Cherry,
            BonusSlotSymbol.Grape, BonusSlotSymbol.Cherry, BonusSlotSymbol.Cherry };
        var spin = BonusSlotRules.PlayFreeSpin(started.State, Grid(freeGrid).Next);
        Assert.IsTrue(spin.Grid.Take(3).All(symbol => symbol == expanding));
        Assert.IsTrue(spin.LineWins.Any(win => win.Symbol == BonusSlotSymbol.Cherry && win.Payout > 0));
        Assert.AreEqual(0, spin.ChargedStake);
        Assert.AreEqual(100, spin.State.LockedStake);
    }

    [TestMethod]
    public void RetriggersAddFiveButRespectRemainingAndTotalAwardCaps()
    {
        var start = BonusSlotRules.StartRound(100, Grid(BonusSlotSymbol.Book, BonusSlotSymbol.Book, BonusSlotSymbol.Book).Next);
        var state = start.State with { FreeSpinsRemaining = 19, FreeSpinsAwarded = 29, SpinsPlayed = 10 };
        var result = BonusSlotRules.PlayFreeSpin(state, Grid(BonusSlotSymbol.Book, BonusSlotSymbol.Book, BonusSlotSymbol.Book).Next);
        Assert.AreEqual(20, result.State.FreeSpinsAwarded);
        Assert.AreEqual(20, result.State.FreeSpinsRemaining);
        Assert.AreEqual(0, result.ChargedStake);
    }

    [TestMethod]
    public void MaximumExposureIsBoundedAndArithmeticOverflowIsRejected()
    {
        Assert.AreEqual((long)BonusSlotRules.MaximumPerSpinPayout(100) * 31,
            BonusSlotRules.MaximumRoundExposure(100));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BonusSlotRules.MaximumRoundExposure(int.MaxValue));
        Assert.ThrowsExactly<ArgumentException>(() => BonusSlotRules.EvaluateGrid(100, new BonusSlotSymbol[14]));
    }

    private static Sequence Grid(params BonusSlotSymbol[] books)
    {
        var symbols = Enumerable.Repeat(BonusSlotSymbol.Cherry, 15).ToArray();
        foreach (var (symbol, i) in books.Select((symbol, i) => (symbol, i))) symbols[i] = symbol;
        return Grid(symbols);
    }

    private static Sequence Grid(BonusSlotSymbol[] symbols) => new(symbols.Select(s => (int)s).ToArray());

    private sealed class Sequence(int[] values)
    {
        private int index;
        public int Next(int max)
        {
            if (index >= values.Length) return 0;
            var value = values[index++];
            return Math.Clamp(value, 0, max - 1);
        }
    }
}