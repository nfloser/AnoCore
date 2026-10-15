using System.Collections.Immutable;

namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Versioned 5x3 collector slot; persisted v2 rounds retain BiggestBoyRules.</summary>
public static class BiggestBoyCollectorRules
{
    public const string Version = "anocore-biggest-boy-5x3-collector-v3";
    public const int Rows = 3;
    public const int MaximumFreeSpinsAwarded = 50;
    public const int PaylineCount = 10;
    public const int MaximumRoundMultiplier = 5000;
    private static readonly int[][] Lines =
    [
        [0, 3, 6, 9, 12], [1, 4, 7, 10, 13], [2, 5, 8, 11, 14],
        [0, 4, 8, 10, 12], [2, 4, 6, 10, 14],
        [0, 3, 7, 9, 12], [2, 5, 7, 11, 14],
        [1, 3, 6, 9, 13], [1, 5, 8, 11, 13], [0, 4, 7, 10, 12],
    ];
    private static readonly int[][] Pays =
    [
        [270, 1350, 3000], [204, 810, 2700], [162, 540, 2028], [136, 406, 1350],
        [82, 270, 810], [67, 203, 676], [26, 67, 203], [26, 67, 203], [13, 54, 136], [13, 54, 136],
    ];
    public static IReadOnlyList<IReadOnlyList<int>> Paylines { get; } =
        Array.AsReadOnly(Lines.Select(line => (IReadOnlyList<int>)Array.AsReadOnly(line)).ToArray());
    public static IReadOnlyList<IReadOnlyList<BiggestBoySymbol>> ReelStrips => BiggestBoyRules.ReelStrips;
    public static int MultiplierForCollectors(int collected) => collected switch { < 4 => 1, < 8 => 2, < 12 => 3, _ => 10 };
    public static int LineMultiplier(BiggestBoySymbol symbol, int count) =>
        symbol is >= BiggestBoySymbol.Kebap and < BiggestBoySymbol.Scatter && count is >= 3 and <= 5 ? Pays[(int)symbol][count - 3] : 0;

    // Published round ceiling ends the feature immediately, including remaining free spins.
    public static long MaximumRoundExposure(int stake)
    {
        ValidateStake(stake);
        return checked(stake * (long)MaximumRoundMultiplier);
    }

    public static BiggestBoySpinResult StartRound(int stake, Func<int, int> random)
    {
        ValidateStake(stake);
        var (grid, stops) = Draw(random);
        var result = EvaluateGrid(stake, grid);
        var free = BiggestBoyRules.FreeSpinsForScatters(result.ScatterCount);
        return result with { Stops = stops, ChargedStake = stake,
            State = new(stake, free, free, 1, result.Payout, Version, null) };
    }

    public static BiggestBoySpinResult PlayFreeSpin(BiggestBoyRoundState state, Func<int, int> random)
    {
        ValidateState(state);
        var (grid, stops) = Draw(random);
        var result = EvaluateGrid(state.LockedStake, grid);
        var values = ImmutableArray.CreateBuilder<long>(15);
        foreach (var symbol in grid)
        {
            var roll = symbol == BiggestBoySymbol.Beer ? Next(random, 100) : -1;
            values.Add(roll < 0 ? 0 : checked(state.LockedStake * (roll < 80 ? 1L : roll < 98 ? 2L : 5L)));
        }
        var wilds = grid.Count(symbol => symbol == BiggestBoySymbol.Kebap);
        var multiplier = MultiplierForCollectors(state.Collectors);
        var collection = checked(values.Sum() * wilds * multiplier);
        var collected = Math.Min(12, state.Collectors + wilds);
        var retriggers = collected / 4 - state.Collectors / 4;
        var payout = Math.Min(checked(result.Payout + collection), MaximumRoundExposure(state.LockedStake) - state.TotalPayout);
        var atLimit = state.TotalPayout + payout == MaximumRoundExposure(state.LockedStake);
        return result with
        {
            Stops = stops, MoneyValues = values.ToImmutable(), CollectionPayout = collection, WinLimitReached = atLimit,
            CollectionMultiplier = multiplier,
            State = state with
            {
                FreeSpinsRemaining = atLimit ? 0 : state.FreeSpinsRemaining - 1 + retriggers * 10,
                FreeSpinsAwarded = state.FreeSpinsAwarded + retriggers * 10,
                SpinsPlayed = state.SpinsPlayed + 1, Collectors = collected,
                TotalPayout = checked(state.TotalPayout + payout),
            },
            Payout = payout,
        };
    }

    public static BiggestBoySpinResult EvaluateGrid(int stake, IReadOnlyList<BiggestBoySymbol> grid)
    {
        ValidateStake(stake);
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.Count != 15 || grid.Any(symbol => !Enum.IsDefined(symbol)))
            throw new ArgumentException("Expected 15 symbols.", nameof(grid));
        var wins = ImmutableArray.CreateBuilder<BiggestBoyLineWin>();
        for (var line = 0; line < Lines.Length; line++)
        {
            BiggestBoyLineWin? best = null;
            for (var candidate = BiggestBoySymbol.Kebap; candidate < BiggestBoySymbol.Scatter; candidate++)
            {
                var count = 0;
                foreach (var cell in Lines[line])
                {
                    if (grid[cell] != candidate && grid[cell] != BiggestBoySymbol.Kebap) break;
                    count++;
                }
                if (count < 3) continue;
                var payout = checked(stake / PaylineCount * (long)LineMultiplier(candidate, count));
                if (best is null || payout > best.Payout) best = new(line, candidate, count, payout);
            }
            if (best is not null) wins.Add(best);
        }
        var total = wins.Sum(win => win.Payout);
        var immutable = grid.ToImmutableArray();
        return new(immutable, [], grid.Count(symbol => symbol == BiggestBoySymbol.Scatter), [],
            wins.ToImmutable(), total, 0, new(stake, 0, 0, 0, total, Version, null), immutable);
    }

    private static (ImmutableArray<BiggestBoySymbol> Grid, ImmutableArray<int> Stops) Draw(Func<int, int> random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var grid = ImmutableArray.CreateBuilder<BiggestBoySymbol>(15);
        var stops = ImmutableArray.CreateBuilder<int>(5);
        for (var reel = 0; reel < 5; reel++)
        {
            var stop = Next(random, 100);
            stops.Add(stop);
            for (var row = 0; row < Rows; row++) grid.Add(ReelStrips[reel][(stop + row) % 100]);
        }
        return (grid.ToImmutable(), stops.ToImmutable());
    }

    private static int Next(Func<int, int> random, int bound)
    {
        var value = random(bound);
        return value >= 0 && value < bound ? value : throw new ArgumentException("Invalid random draw.", nameof(random));
    }

    private static void ValidateStake(int stake)
    {
        if (!BiggestBoyRules.IsValidStake(stake)) throw new ArgumentOutOfRangeException(nameof(stake));
    }

    private static void ValidateState(BiggestBoyRoundState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateStake(state.LockedStake);
        if (state.RulesVersion != Version || state.ExpandingSymbol is not null
            || state.Collectors is < 0 or > 12 || state.FreeSpinsRemaining is <= 0 or > MaximumFreeSpinsAwarded
            || state.FreeSpinsAwarded is < 10 or > MaximumFreeSpinsAwarded
            || state.SpinsPlayed is < 1 or > MaximumFreeSpinsAwarded
            || state.FreeSpinsAwarded - state.FreeSpinsRemaining != state.SpinsPlayed - 1
            || state.FreeSpinsAwarded - state.Collectors / 4 * 10 is not (10 or 15 or 20)
            || state.TotalPayout < 0 || state.TotalPayout > MaximumRoundExposure(state.LockedStake))
            throw new ArgumentException("Invalid collector bonus state.", nameof(state));
    }
}
