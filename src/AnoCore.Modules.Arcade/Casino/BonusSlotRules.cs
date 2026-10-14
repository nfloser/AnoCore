using System.Collections.Immutable;

namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Original, deterministic 5x3 book-slot rules; independent of the legacy anocasino-v1 game.</summary>
public enum BonusSlotSymbol { Cherry, Lemon, Plum, Orange, Grape, Bell, Seven, Book }

public sealed record BonusSlotLineWin(int Payline, BonusSlotSymbol Symbol, int MatchCount, long Payout);

/// <summary>The bet paid for the triggering spin remains fixed for every free spin.</summary>
public sealed record BonusSlotRoundState(
    int LockedStake,
    int FreeSpinsRemaining,
    int FreeSpinsAwarded,
    int SpinsPlayed,
    long TotalPayout,
    BonusSlotSymbol? ExpandingSymbol,
    string RulesVersion);

public sealed record BonusSlotSpinResult(
    ImmutableArray<BonusSlotSymbol> Grid,
    int ScatterCount,
    ImmutableArray<BonusSlotLineWin> LineWins,
    long Payout,
    int ChargedStake,
    BonusSlotRoundState State);

public static class BonusSlotRules
{
    public const string Version = "anocore-book-5x3-v1";
    public const int ReelCount = 5;
    public const int RowsPerReel = 3;
    public const int GridSize = ReelCount * RowsPerReel;
    public const int PaylineCount = 10;
    public const int TriggerScatterCount = 3;
    public const int InitialFreeSpins = 10;
    public const int RetriggerFreeSpins = 5;
    public const int MaximumFreeSpinsRemaining = 20;
    public const int MaximumFreeSpinsAwarded = 30;
    private const int MaximumStake = 1_000_000;

    // Rows are indexed top-to-bottom within each reel; grid index = reel * 3 + row.
    private static readonly int[][] Lines =
    [
        [0, 3, 6, 9, 12], [1, 4, 7, 10, 13], [2, 5, 8, 11, 14],
        [0, 4, 8, 10, 12], [2, 4, 6, 10, 14],
        [0, 3, 7, 11, 14], [2, 5, 7, 9, 12],
        [1, 3, 7, 11, 13], [1, 5, 7, 9, 13],
        [0, 4, 7, 10, 14],
    ];

    // Each of the eight symbols has equal one-in-eight weight. Book is both scatter and wild.
    private static readonly int[] Weights = [1, 1, 1, 1, 1, 1, 1, 1];
    private static readonly int[] WeightStarts = [0, 1, 2, 3, 4, 5, 6, 7];

    // Original per-line multipliers, indexed by ordinary symbol then 3/4/5 consecutive matches.
    private static readonly int[][] Paytable =
    [
        [2, 5, 10], [1, 4, 8], [3, 6, 12], [4, 8, 20],
        [5, 10, 30], [8, 20, 60], [15, 50, 200],
    ];

    public static IReadOnlyList<IReadOnlyList<int>> Paylines { get; } =
        Lines.Select(line => (IReadOnlyList<int>)Array.AsReadOnly(line)).ToArray();

    /// <summary>
    /// Maximum single-spin line exposure, including all ten lines winning simultaneously.
    /// Stake is divided evenly across ten lines; stakes must therefore be multiples of ten.
    /// </summary>
    public static long MaximumPerSpinPayout(int stake)
    {
        ValidateStake(stake);
        return checked((long)stake * Paytable.Max(row => row[2]));
    }

    /// <summary>Maximum first paid spin plus the absolute cap of 30 free spins.</summary>
    public static long MaximumRoundExposure(int stake) =>
        checked(MaximumPerSpinPayout(stake) * (MaximumFreeSpinsAwarded + 1L));

    public static BonusSlotSpinResult StartRound(int stake, Func<int, int> random)
    {
        ValidateStake(stake);
        ArgumentNullException.ThrowIfNull(random);
        var grid = DrawGrid(random);
        var evaluated = EvaluateGrid(stake, grid);
        var expanding = evaluated.ScatterCount >= TriggerScatterCount
            ? DrawExpandingSymbol(random) : (BonusSlotSymbol?)null;
        var awarded = expanding.HasValue ? InitialFreeSpins : 0;
        var state = new BonusSlotRoundState(stake, awarded, awarded, 1, evaluated.Payout, expanding, Version);
        return evaluated with { ChargedStake = stake, State = state };
    }

    public static BonusSlotSpinResult PlayFreeSpin(BonusSlotRoundState state, Func<int, int> random)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(random);
        if (state.LockedStake is < 10 or > MaximumStake || state.LockedStake % 10 != 0
            || state.FreeSpinsRemaining <= 0 || state.FreeSpinsRemaining > MaximumFreeSpinsRemaining
            || state.FreeSpinsAwarded < state.FreeSpinsRemaining || state.FreeSpinsAwarded > MaximumFreeSpinsAwarded
            || state.SpinsPlayed < 1 || state.SpinsPlayed > MaximumFreeSpinsAwarded + 1
            || state.FreeSpinsAwarded - state.FreeSpinsRemaining != state.SpinsPlayed - 1
            || (state.FreeSpinsAwarded == 0) != (state.ExpandingSymbol is null)
            || state.ExpandingSymbol is BonusSlotSymbol.Book || state.RulesVersion != Version
            || state.TotalPayout < 0 || state.TotalPayout > MaximumRoundExposure(state.LockedStake))
            throw new ArgumentException("Invalid bonus-slot round state.", nameof(state));

        var grid = DrawGrid(random);
        if (state.ExpandingSymbol is { } expanding)
        {
            for (var reel = 0; reel < ReelCount; reel++)
            {
                var offset = reel * RowsPerReel;
                if (grid[offset] == expanding || grid[offset + 1] == expanding || grid[offset + 2] == expanding)
                    grid[offset] = grid[offset + 1] = grid[offset + 2] = expanding;
            }
        }

        var evaluated = EvaluateGrid(state.LockedStake, grid);
        var nextRemaining = state.FreeSpinsRemaining - 1;
        var awarded = state.FreeSpinsAwarded;
        var retrigger = evaluated.ScatterCount >= TriggerScatterCount
            ? Math.Min(RetriggerFreeSpins, Math.Min(MaximumFreeSpinsAwarded - awarded,
                MaximumFreeSpinsRemaining - nextRemaining)) : 0;
        awarded += retrigger;
        nextRemaining += retrigger;
        var nextState = state with
        {
            FreeSpinsRemaining = nextRemaining,
            FreeSpinsAwarded = awarded,
            SpinsPlayed = checked(state.SpinsPlayed + 1),
            TotalPayout = checked(state.TotalPayout + evaluated.Payout),
        };
        return evaluated with { ChargedStake = 0, State = nextState };
    }

    public static BonusSlotSpinResult EvaluateGrid(int stake, IReadOnlyList<BonusSlotSymbol> grid)
    {
        ValidateStake(stake);
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.Count != GridSize || grid.Any(symbol => !Enum.IsDefined(symbol)))
            throw new ArgumentException("Grid must contain exactly 15 valid symbols.", nameof(grid));

        var copy = grid.ToImmutableArray();
        var scatters = copy.Count(symbol => symbol == BonusSlotSymbol.Book);
        var wins = ImmutableArray.CreateBuilder<BonusSlotLineWin>();
        var lineStake = stake / PaylineCount;
        long payout = 0;

        for (var lineIndex = 0; lineIndex < Lines.Length; lineIndex++)
        {
            var line = Lines[lineIndex];
            BonusSlotLineWin? best = null;
            for (var candidate = 0; candidate < (int)BonusSlotSymbol.Book; candidate++)
            {
                var symbol = (BonusSlotSymbol)candidate;
                var count = 0;
                foreach (var cell in line)
                {
                    var actual = copy[cell];
                    if (actual != symbol && actual != BonusSlotSymbol.Book) break;
                    count++;
                }
                if (count < 3) continue;
                var value = checked((long)lineStake * Paytable[candidate][count - 3]);
                if (best is null || value > best.Payout)
                    best = new BonusSlotLineWin(lineIndex, symbol, count, value);
            }

            // An all-Book line is a wild line, paid at the highest original symbol's rate.
            if (best is null && line.All(cell => copy[cell] == BonusSlotSymbol.Book))
                best = new BonusSlotLineWin(lineIndex, BonusSlotSymbol.Seven, 5, checked((long)lineStake * Paytable[6][2]));
            if (best is not null)
            {
                wins.Add(best);
                payout = checked(payout + best.Payout);
            }
        }

        return new BonusSlotSpinResult(copy, scatters, wins.ToImmutable(), payout, 0,
            new BonusSlotRoundState(stake, 0, 0, 0, payout, null, Version));
    }

    private static BonusSlotSymbol[] DrawGrid(Func<int, int> random)
    {
        var grid = new BonusSlotSymbol[GridSize];
        for (var cell = 0; cell < grid.Length; cell++)
        {
            var pick = Next(random, Weights.Sum());
            var symbol = 0;
            while (symbol < Weights.Length - 1 && pick >= WeightStarts[symbol] + Weights[symbol]) symbol++;
            grid[cell] = (BonusSlotSymbol)symbol;
        }
        return grid;
    }

    private static BonusSlotSymbol DrawExpandingSymbol(Func<int, int> random)
    {
        var selected = Next(random, (int)BonusSlotSymbol.Book);
        return (BonusSlotSymbol)selected;
    }

    private static int Next(Func<int, int> random, int exclusiveMax)
    {
        var value = random(exclusiveMax);
        if (value < 0 || value >= exclusiveMax)
            throw new ArgumentException("Random source must return a value in [0, max).", nameof(random));
        return value;
    }

    private static void ValidateStake(int stake)
    {
        if (stake < 10 || stake > MaximumStake || stake % 10 != 0)
            throw new ArgumentOutOfRangeException(nameof(stake), "Stake must be 10..1,000,000 in multiples of ten.");
    }
}
