using System.Collections.Immutable;

namespace AnoCore.Modules.Arcade.Casino;

public enum BiggestBoySymbol { Kebap, Beer, Cigarette, Butter, Shot, Funnel, G, O, A, T, Scatter }
public sealed record BiggestBoyLineWin(int Payline, BiggestBoySymbol Symbol, int MatchCount, long Payout);
public sealed record BiggestBoyRoundState(int LockedStake, int FreeSpinsRemaining, int FreeSpinsAwarded,
    int SpinsPlayed, long TotalPayout, string RulesVersion);
public sealed record BiggestBoySpinResult(ImmutableArray<BiggestBoySymbol> Grid, ImmutableArray<int> Stops,
    int ScatterCount, ImmutableArray<int> ExpandedReels, ImmutableArray<BiggestBoyLineWin> LineWins,
    long Payout, int ChargedStake, BiggestBoyRoundState State);

/// <summary>Original 5x5 virtual-point slot. One uniform stop per fixed circular reel.</summary>
public static class BiggestBoyRules
{
    public const string Version = "anocore-biggest-boy-5x5-v1";
    public const int MaximumFreeSpinsAwarded = 30;
    public const int PaylineCount = 10;
    public const int MaximumStake = 10_000;
    private static readonly int[][] Lines =
    [
        [0, 5, 10, 15, 20], [1, 6, 11, 16, 21], [2, 7, 12, 17, 22],
        [3, 8, 13, 18, 23], [4, 9, 14, 19, 24],
        [0, 6, 12, 18, 24], [4, 8, 12, 16, 20],
        [0, 6, 12, 16, 20], [4, 8, 12, 18, 24], [2, 6, 10, 16, 22],
    ];
    private static readonly int[][] Pays =
    [
        [225, 1125, 2500], [170, 675, 2250], [135, 450, 1690], [113, 338, 1125],
        [68, 225, 675], [56, 169, 563], [22, 56, 169], [22, 56, 169], [11, 45, 113], [11, 45, 113],
    ];
    public static IReadOnlyList<IReadOnlyList<int>> Paylines { get; } =
        Array.AsReadOnly(Lines.Select(line => (IReadOnlyList<int>)Array.AsReadOnly(line)).ToArray());
    public static IReadOnlyList<IReadOnlyList<BiggestBoySymbol>> ReelStrips { get; } =
        Array.AsReadOnly(Enumerable.Range(0, 5).Select(reel => (IReadOnlyList<BiggestBoySymbol>)Array.AsReadOnly(BuildStrip(reel))).ToArray());

    public static int Multiplier(BiggestBoySymbol symbol, int count) =>
        symbol is >= BiggestBoySymbol.Kebap and < BiggestBoySymbol.Scatter && count is >= 3 and <= 5
            ? Pays[(int)symbol][count - 3] : 0;
    public static long MaximumRoundExposure(int stake)
    {
        ValidateStake(stake);
        return checked(stake * (long)Pays.Max(row => row[2]) * 31);
    }

    public static BiggestBoySymbol[] DrawGrid(Func<int, int> random) => Draw(random).Grid;
    private static (BiggestBoySymbol[] Grid, int[] Stops) Draw(Func<int, int> random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var grid = new BiggestBoySymbol[25];
        var stops = new int[5];
        for (var reel = 0; reel < 5; reel++)
        {
            var stop = random(100);
            if (stop is < 0 or >= 100) throw new ArgumentException("Invalid reel stop.", nameof(random));
            stops[reel] = stop;
            for (var row = 0; row < 5; row++) grid[reel * 5 + row] = ReelStrips[reel][(stop + row) % 100];
        }
        return (grid, stops);
    }

    public static BiggestBoySpinResult StartRound(int stake, Func<int, int> random)
    {
        ValidateStake(stake);
        var drawn = Draw(random);
        var result = EvaluateGrid(stake, drawn.Grid);
        var free = result.ScatterCount >= 3 ? 10 : 0;
        return result with
        {
            Stops = drawn.Stops.ToImmutableArray(),
            ChargedStake = stake,
            State = new(stake, free, free, 1, result.Payout, Version)
        };
    }

    public static BiggestBoySpinResult PlayFreeSpin(BiggestBoyRoundState state, Func<int, int> random)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateStake(state.LockedStake);
        if (state.RulesVersion != Version || state.FreeSpinsRemaining is < 1 or > 20
            || state.FreeSpinsAwarded is < 10 or > 30 || state.FreeSpinsAwarded < state.FreeSpinsRemaining
            || state.SpinsPlayed is < 1 or > 30
            || state.FreeSpinsAwarded - state.FreeSpinsRemaining != state.SpinsPlayed - 1
            || state.TotalPayout < 0 || state.TotalPayout > MaximumRoundExposure(state.LockedStake))
            throw new ArgumentException("Invalid Biggest Boy bonus state.", nameof(state));
        var drawn = Draw(random);
        // Count the raw scatters before expansion; preserve their cells so bonus triggers remain visible.
        var expanded = ImmutableArray.CreateBuilder<int>();
        for (var reel = 0; reel < 5; reel++)
        {
            if (!drawn.Grid.Skip(reel * 5).Take(5).Contains(BiggestBoySymbol.Kebap)) continue;
            expanded.Add(reel);
            for (var row = 0; row < 5; row++)
                if (drawn.Grid[reel * 5 + row] != BiggestBoySymbol.Scatter)
                    drawn.Grid[reel * 5 + row] = BiggestBoySymbol.Kebap;
        }
        var result = EvaluateGrid(state.LockedStake, drawn.Grid);
        var remaining = state.FreeSpinsRemaining - 1;
        var added = result.ScatterCount >= 3 ? Math.Min(5, Math.Min(30 - state.FreeSpinsAwarded, 20 - remaining)) : 0;
        return result with
        {
            Stops = drawn.Stops.ToImmutableArray(),
            ExpandedReels = expanded.ToImmutable(),
            State = state with
            {
                FreeSpinsRemaining = remaining + added,
                FreeSpinsAwarded = state.FreeSpinsAwarded + added,
                SpinsPlayed = state.SpinsPlayed + 1,
                TotalPayout = checked(state.TotalPayout + result.Payout)
            }
        };
    }

    public static BiggestBoySpinResult EvaluateGrid(int stake, IReadOnlyList<BiggestBoySymbol> grid)
    {
        ValidateStake(stake);
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.Count != 25 || grid.Any(symbol => !Enum.IsDefined(symbol))) throw new ArgumentException("Expected 25 symbols.", nameof(grid));
        var wins = ImmutableArray.CreateBuilder<BiggestBoyLineWin>();
        for (var lineIndex = 0; lineIndex < Lines.Length; lineIndex++)
        {
            BiggestBoyLineWin? best = null;
            for (var candidate = BiggestBoySymbol.Kebap; candidate < BiggestBoySymbol.Scatter; candidate++)
            {
                var count = 0;
                foreach (var cell in Lines[lineIndex])
                {
                    if (grid[cell] != candidate && grid[cell] != BiggestBoySymbol.Kebap) break;
                    count++;
                }
                if (count < 3) continue;
                var payout = checked(stake / PaylineCount * (long)Multiplier(candidate, count));
                if (best is null || payout > best.Payout) best = new(lineIndex, candidate, count, payout);
            }
            if (best is not null) wins.Add(best);
        }
        var total = wins.Sum(win => win.Payout);
        return new(grid.ToImmutableArray(), [], grid.Count(symbol => symbol == BiggestBoySymbol.Scatter), [],
            wins.ToImmutable(), total, 0, new(stake, 0, 0, 0, total, Version));
    }

    private static BiggestBoySymbol[] BuildStrip(int reel)
    {
        var normals = new List<BiggestBoySymbol>();
        int[] counts = [6, 6, 7, 10, 10, 14, 14, 15, 15];
        for (var symbol = 1; symbol <= 9; symbol++) normals.AddRange(Enumerable.Repeat((BiggestBoySymbol)symbol, counts[symbol - 1]));
        var strip = new BiggestBoySymbol[100];
        strip[0] = strip[50] = BiggestBoySymbol.Scatter;
        strip[5] = BiggestBoySymbol.Kebap;
        var index = 0;
        for (var position = 0; position < 100; position++)
            if (position is not (0 or 5 or 50)) strip[position] = normals[(index++ * 37 + reel * 19) % 97];
        return strip;
    }

    private static void ValidateStake(int stake)
    {
        if (stake is < 50 or > MaximumStake || stake % 50 != 0) throw new ArgumentOutOfRangeException(nameof(stake));
    }
}
