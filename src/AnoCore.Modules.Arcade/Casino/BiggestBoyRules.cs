using System.Collections.Immutable;

namespace AnoCore.Modules.Arcade.Casino;

public enum BiggestBoySymbol { Kebap, Beer, Cigarette, Butter, Shot, Funnel, G, O, A, T, Scatter, Book }
public sealed record BiggestBoyLineWin(int Payline, BiggestBoySymbol Symbol, int MatchCount, long Payout);
public sealed record BiggestBoyRoundState(int LockedStake, int FreeSpinsRemaining, int FreeSpinsAwarded,
    int SpinsPlayed, long TotalPayout, string RulesVersion, BiggestBoySymbol? ExpandingSymbol, int Collectors = 0);
public sealed record BiggestBoySpinResult(ImmutableArray<BiggestBoySymbol> Grid, ImmutableArray<int> Stops,
    int ScatterCount, ImmutableArray<int> ExpandedReels, ImmutableArray<BiggestBoyLineWin> LineWins,
    long Payout, int ChargedStake, BiggestBoyRoundState State,
    ImmutableArray<BiggestBoySymbol> RawGrid, ImmutableArray<long> MoneyValues = default,
    long CollectionPayout = 0, int CollectionMultiplier = 1, bool WinLimitReached = false)
{
    public ImmutableArray<long> MoneyValues { get; init; } = MoneyValues.IsDefault ? [] : MoneyValues;
}

/// <summary>Original 5x5 virtual-currency slot. One uniform stop per fixed circular reel.</summary>
public static class BiggestBoyRules
{
    public const string Version = "anocore-biggest-boy-5x5-v2";
    public const int MaximumFreeSpinsAwarded = 30;
    public const int PaylineCount = 25;
    public const int MaximumStake = 10_000;
    public static IReadOnlyList<int> StakeSteps { get; } = Array.AsReadOnly(new[] { 100, 250, 500, 1_000, 2_500, 5_000, 10_000 });

    private static readonly int[][] Lines =
    [
        [0, 5, 10, 15, 20], [1, 6, 11, 16, 21], [2, 7, 12, 17, 22], [3, 8, 13, 18, 23], [4, 9, 14, 19, 24],
        [0, 6, 12, 18, 24], [4, 8, 12, 16, 20], [0, 6, 12, 16, 20], [4, 8, 12, 18, 24], [2, 6, 10, 16, 22],
        [2, 8, 14, 18, 22], [0, 5, 11, 15, 20], [4, 9, 13, 19, 24], [1, 5, 11, 17, 21], [3, 9, 13, 17, 23],
        [1, 7, 13, 17, 21], [3, 7, 11, 17, 23], [0, 6, 11, 16, 20], [4, 8, 13, 18, 24], [2, 7, 11, 17, 22],
        [2, 7, 13, 17, 22], [1, 6, 12, 16, 21], [3, 8, 12, 18, 23], [0, 7, 14, 17, 20], [4, 7, 10, 17, 24],
    ];

    private static readonly int[][] Pays =
    [
        [225, 1125, 2500], [170, 675, 2250], [135, 450, 1690], [113, 338, 1125],
        [68, 225, 675], [56, 169, 563], [22, 56, 169], [22, 56, 169], [11, 45, 113], [11, 45, 113],
    ];
    // Extra payout for the selected expanding symbol. These values are deliberately
    // separate from the base paytable so frequent low symbols cannot inflate bonus RTP.
    private static readonly int[][] ExpansionPays =
    [
        [0, 0, 0], [15, 59, 198], [16, 54, 204], [11, 32, 106], [2, 8, 24],
        [3, 8, 26], [1, 2, 6], [1, 2, 6], [1, 2, 5], [1, 2, 5],
    ];

    public static IReadOnlyList<IReadOnlyList<int>> Paylines { get; } =
        Array.AsReadOnly(Lines.Select(line => (IReadOnlyList<int>)Array.AsReadOnly(line)).ToArray());
    public static IReadOnlyList<IReadOnlyList<BiggestBoySymbol>> ReelStrips { get; } =
        Array.AsReadOnly(Enumerable.Range(0, 5).Select(reel => (IReadOnlyList<BiggestBoySymbol>)Array.AsReadOnly(BuildStrip(reel))).ToArray());

    public static int Multiplier(BiggestBoySymbol symbol, int count) =>
        symbol is >= BiggestBoySymbol.Kebap and < BiggestBoySymbol.Scatter && count is >= 3 and <= 5
            ? Pays[(int)symbol][count - 3] : 0;

    public static int ExpansionMultiplier(BiggestBoySymbol symbol, int count) =>
        IsSelectableExpandingSymbol(symbol) && count is >= 3 and <= 5
            ? ExpansionPays[(int)symbol][count - 3] : 0;

    public static int FreeSpinsForScatters(int scatterCount) => scatterCount switch
    {
        3 => 10,
        4 => 15,
        >= 5 => 20,
        _ => 0,
    };

    public static bool IsSelectableExpandingSymbol(BiggestBoySymbol symbol) =>
        symbol is >= BiggestBoySymbol.Beer and <= BiggestBoySymbol.T;

    public static bool IsValidStake(int stake) => StakeSteps.Contains(stake);

    public static int ClampStakeToConfiguredMaximum(int stake, int configuredMaximum)
    {
        var ceiling = Math.Clamp(configuredMaximum, StakeSteps[0], MaximumStake);
        return StakeSteps.Where(value => value <= ceiling).DefaultIfEmpty(StakeSteps[0])
            .OrderBy(value => Math.Abs(value - stake)).ThenBy(value => value).First();
    }

    public static int StepStake(int current, int direction, int configuredMaximum)
    {
        var ceiling = Math.Clamp(configuredMaximum, StakeSteps[0], MaximumStake);
        var available = StakeSteps.Where(value => value <= ceiling).ToArray();
        var index = Array.IndexOf(available, current);
        if (index < 0) index = Array.FindLastIndex(available, value => value <= current);
        index = Math.Clamp(index + Math.Sign(direction), 0, available.Length - 1);
        return available[index];
    }

    public static long MaximumRoundExposure(int stake)
    {
        ValidateStake(stake);
        return checked(stake * ((long)Pays.Max(row => row[2]) + ExpansionPays.Max(row => row[2])) * 31);
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
        var free = FreeSpinsForScatters(result.ScatterCount);
        return result with
        {
            Stops = drawn.Stops.ToImmutableArray(),
            ChargedStake = stake,
            State = new(stake, free, free, 1, result.Payout, Version, null),
            RawGrid = drawn.Grid.ToImmutableArray(),
        };
    }

    public static BiggestBoySpinResult SelectExpandingSymbol(BiggestBoySpinResult current, BiggestBoySymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(current);
        ValidateState(current.State);
        if (current.State.FreeSpinsRemaining <= 0 || current.State.ExpandingSymbol is not null || !IsSelectableExpandingSymbol(symbol))
            throw new ArgumentException("Ungültige Bonus-Symbolauswahl.", nameof(symbol));
        return current with { ChargedStake = 0, State = current.State with { ExpandingSymbol = symbol } };
    }

    public static BiggestBoySpinResult PlayFreeSpin(BiggestBoyRoundState state, Func<int, int> random)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateState(state);
        if (state.FreeSpinsRemaining <= 0 || state.ExpandingSymbol is not { } expanding || !IsSelectableExpandingSymbol(expanding))
            throw new ArgumentException("Vor dem Freispiel muss ein gültiges Bonussymbol gewählt sein.", nameof(state));

        var drawn = Draw(random);
        var raw = drawn.Grid.ToArray();
        var expandedGrid = drawn.Grid.ToArray();
        var expanded = ImmutableArray.CreateBuilder<int>();
        for (var reel = 0; reel < 5; reel++)
        {
            if (!raw.Skip(reel * 5).Take(5).Contains(expanding)) continue;
            expanded.Add(reel);
            for (var row = 0; row < 5; row++)
                if (expandedGrid[reel * 5 + row] != BiggestBoySymbol.Scatter)
                    expandedGrid[reel * 5 + row] = expanding;
        }

        var baseResult = EvaluateGrid(state.LockedStake, raw);
        var expansionWins = EvaluateExpansionWins(state.LockedStake, expandedGrid, expanding);
        var lineWins = baseResult.LineWins.AddRange(expansionWins);
        var payout = checked(baseResult.Payout + expansionWins.Sum(win => win.Payout));
        var scatterCount = raw.Count(symbol => symbol == BiggestBoySymbol.Scatter);
        var remaining = state.FreeSpinsRemaining - 1;
        var added = scatterCount >= 3 ? Math.Min(5, MaximumFreeSpinsAwarded - state.FreeSpinsAwarded) : 0;
        return baseResult with
        {
            RawGrid = raw.ToImmutableArray(),
            Grid = expandedGrid.ToImmutableArray(),
            Stops = drawn.Stops.ToImmutableArray(),
            ScatterCount = scatterCount,
            ExpandedReels = expanded.ToImmutable(),
            LineWins = lineWins,
            Payout = payout,
            State = state with
            {
                FreeSpinsRemaining = remaining + added,
                FreeSpinsAwarded = state.FreeSpinsAwarded + added,
                SpinsPlayed = state.SpinsPlayed + 1,
                TotalPayout = checked(state.TotalPayout + payout),
            },
        };
    }

    public static BiggestBoySpinResult EvaluateGrid(int stake, IReadOnlyList<BiggestBoySymbol> grid)
    {
        ValidateStake(stake);
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.Count != 25 || grid.Any(symbol => !Enum.IsDefined(symbol)))
            throw new ArgumentException("Expected 25 symbols.", nameof(grid));

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
        var immutable = grid.ToImmutableArray();
        return new(immutable, [], grid.Count(symbol => symbol == BiggestBoySymbol.Scatter), [],
            wins.ToImmutable(), total, 0, new(stake, 0, 0, 0, total, Version, null), immutable);
    }

    private static ImmutableArray<BiggestBoyLineWin> EvaluateExpansionWins(
        int stake, IReadOnlyList<BiggestBoySymbol> grid, BiggestBoySymbol selected)
    {
        var wins = ImmutableArray.CreateBuilder<BiggestBoyLineWin>();
        for (var lineIndex = 0; lineIndex < Lines.Length; lineIndex++)
        {
            var count = 0;
            foreach (var cell in Lines[lineIndex])
            {
                if (grid[cell] != selected && grid[cell] != BiggestBoySymbol.Kebap) break;
                count++;
            }
            if (count < 3) continue;
            var payout = checked(stake / PaylineCount * (long)ExpansionMultiplier(selected, count));
            if (payout > 0) wins.Add(new(lineIndex, selected, count, payout));
        }
        return wins.ToImmutable();
    }

    private static BiggestBoySymbol[] BuildStrip(int reel)
    {
        var normals = new List<BiggestBoySymbol>();
        int[] counts = [6, 6, 7, 10, 10, 14, 14, 15, 15];
        for (var symbol = 1; symbol <= 9; symbol++)
            normals.AddRange(Enumerable.Repeat((BiggestBoySymbol)symbol, counts[symbol - 1]));
        var strip = new BiggestBoySymbol[100];
        strip[0] = strip[50] = BiggestBoySymbol.Scatter;
        strip[5] = BiggestBoySymbol.Kebap;
        var index = 0;
        for (var position = 0; position < 100; position++)
            if (position is not (0 or 5 or 50))
                strip[position] = normals[(index++ * 37 + reel * 19) % 97];
        return strip;
    }

    private static void ValidateState(BiggestBoyRoundState state)
    {
        ValidateStake(state.LockedStake);
        if (state.RulesVersion != Version
            || state.FreeSpinsRemaining is < 0 or > MaximumFreeSpinsAwarded
            || state.FreeSpinsAwarded is < 10 or > MaximumFreeSpinsAwarded
            || state.FreeSpinsAwarded < state.FreeSpinsRemaining
            || state.SpinsPlayed is < 1 or > MaximumFreeSpinsAwarded + 1
            || state.FreeSpinsAwarded - state.FreeSpinsRemaining != state.SpinsPlayed - 1
            || state.TotalPayout < 0 || state.TotalPayout > MaximumRoundExposure(state.LockedStake)
            || state.ExpandingSymbol is { } expanding && !IsSelectableExpandingSymbol(expanding))
            throw new ArgumentException("Invalid Biggest Boy bonus state.", nameof(state));
    }

    private static void ValidateStake(int stake)
    {
        if (!IsValidStake(stake)) throw new ArgumentOutOfRangeException(nameof(stake));
    }
}
