namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Lower stake-relative payouts for new controlled rounds; historical paytables stay frozen.</summary>
public static class BiggestBoyBalancedRules
{
    private static readonly int[][] Pays =
    [
        [5, 15, 50], [4, 12, 40], [3, 10, 30], [2, 8, 25],
        [2, 6, 20], [1, 5, 15], [1, 3, 10], [1, 3, 10],
        [1, 2, 8], [1, 2, 8]
    ];

    public static int Multiplier(BiggestBoySymbol symbol, int count) =>
        BiggestBoyExpandingRules.IsRegular(symbol) && count is >= 3 and <= 5 ? Pays[(int)symbol][count - 3] : 0;

    public static BiggestBoySpinResult EvaluateGrid(int stake, IReadOnlyList<BiggestBoySymbol> grid)
        => BiggestBoyExpandingRules.EvaluateGrid(stake, grid, Multiplier);

    public static long MaximumRoundExposure(int stake) => checked(stake * 50L * 31);
}
