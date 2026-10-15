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

    public static BonusSlotSpinResult EvaluateGrid(int stake, IReadOnlyList<BonusSlotSymbol> grid)
        => BonusSlotRules.EvaluateGrid(stake, grid, Multiplier);

    public static long MaximumRoundExposure(int stake) => checked(stake * 50L * 31);
}
