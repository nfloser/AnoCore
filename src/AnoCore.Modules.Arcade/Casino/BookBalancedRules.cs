using System.Collections.Immutable;

namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Versioned fruit-slot multipliers, expressed per one tenth of the total stake.</summary>
public static class BookBalancedRules
{
    private static readonly int[][] Pays =
    [
        [2, 5, 10], [1, 4, 8], [2, 5, 10], [3, 8, 15],
        [4, 10, 20], [5, 15, 30], [8, 25, 50]
    ];

    public static int Multiplier(BonusSlotSymbol symbol, int count) =>
        symbol is >= BonusSlotSymbol.Cherry and < BonusSlotSymbol.Book && count is >= 3 and <= 5
            ? Pays[(int)symbol][count - 3] : 0;

    public static BonusSlotSpinResult EvaluateGrid(int stake, IReadOnlyList<BonusSlotSymbol> grid,
        SlotPaytableSettings? paytable = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.Count != 15 || grid.Any(symbol => !Enum.IsDefined(symbol)))
            throw new ArgumentException("Expected 15 valid fruit symbols.", nameof(grid));
        paytable?.Validate(7);
        // The frozen Book evaluator stays byte-identical. Both cabinets share these
        // ten lines and wild substitution rules; map only symbols, never money.
        var mapped = grid.Select(symbol => symbol == BonusSlotSymbol.Book
            ? BiggestBoySymbol.Book : (BiggestBoySymbol)(int)symbol).ToArray();
        var result = BiggestBoyExpandingRules.EvaluateGrid(stake, mapped,
            (symbol, count) => (int)symbol is >= 0 and < 7
                ? paytable?.Multiplier((int)symbol, count,
                    Multiplier((BonusSlotSymbol)(int)symbol, count))
                    ?? Multiplier((BonusSlotSymbol)(int)symbol, count)
                : 0,
            paytable?.LineStakeDivisor ?? BonusSlotRules.PaylineCount);
        return new(grid.ToImmutableArray(), grid.Count(symbol => symbol == BonusSlotSymbol.Book),
            result.LineWins.Select(win => new BonusSlotLineWin(win.Payline,
                (BonusSlotSymbol)(int)win.Symbol, win.MatchCount, win.Payout)).ToImmutableArray(),
            result.Payout, 0, new(stake, 0, 0, 0, result.Payout, null, ControlledSlotRules.BookVersion));
    }

    public static long MaximumRoundExposure(int stake, SlotPaytableSettings? paytable = null) =>
        paytable is null ? checked(stake * 50L * 31)
            : paytable.Validate(7).MaximumRoundExposure(stake, 50);
}
