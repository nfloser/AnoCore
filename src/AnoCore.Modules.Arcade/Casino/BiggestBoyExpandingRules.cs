using System.Collections.Immutable;

namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Versioned 5x3 Book-style rules; legacy v2/v3 rules remain frozen.</summary>
public static class BiggestBoyExpandingRules
{
    public const string Version = "anocore-biggest-boy-5x3-expanding-v4";
    public const int ReelCount = 5;
    public const int Rows = 3;
    public const int GridSize = ReelCount * Rows;
    public const int StripLength = 100;
    public const int MaximumFreeSpinsAwarded = 30;
    public const int PaylineCount = BonusSlotRules.PaylineCount;
    public static IReadOnlyList<IReadOnlyList<int>> Paylines => BonusSlotRules.Paylines;
    private static readonly int[] Counts = [4, 5, 6, 7, 9, 10, 14, 14, 14, 14];
    private static readonly int[][] Pays =
    [
        [270, 1350, 3000],
        [216, 945, 2900],
        [162, 675, 2160],
        [135, 473, 1485],
        [81, 297, 878],
        [68, 216, 675],
        [26, 68, 194],
        [26, 68, 194],
        [13, 60, 129],
        [13, 60, 129],
    ];
    public static IReadOnlyList<IReadOnlyList<BiggestBoySymbol>> ReelStrips { get; } =
        Array.AsReadOnly(Enumerable.Range(0, ReelCount).Select(reel => (IReadOnlyList<BiggestBoySymbol>)Array.AsReadOnly(BuildStrip(reel))).ToArray());
    public static int Multiplier(BiggestBoySymbol symbol, int count) => IsRegular(symbol) && count is >= 3 and <= 5 ? Pays[(int)symbol][count - 3] : 0;
    public static bool IsRegular(BiggestBoySymbol symbol) => symbol is >= BiggestBoySymbol.Kebap and <= BiggestBoySymbol.T;
    public static int FreeSpinsForScatters(int count) => count switch { 3 => 10, 4 => 15, >= 5 => 20, _ => 0 };
    public static long MaximumRoundExposure(int stake)
    {
        ValidateStake(stake);
        return checked(stake * (long)Pays.Max(x => x[2]) * (MaximumFreeSpinsAwarded + 1));
    }

    public static BiggestBoySpinResult StartRound(int stake, Func<int, int> random, string profile = BiggestBoyProfiles.Standard)
    {
        ValidateStake(stake);
        BiggestBoyProfiles.Validate(profile);
        var (grid, stops) = Draw(random, profile);
        var result = EvaluateGrid(stake, grid);
        var free = FreeSpinsForScatters(result.ScatterCount);
        var expanding = free > 0 ? (BiggestBoySymbol?)Next(random, Counts.Length) : null;
        return result with
        {
            Stops = stops,
            ChargedStake = stake,
            State = new(stake, free, free, 1, result.Payout, Version, expanding, Profile: profile)
        };
    }

    public static BiggestBoySpinResult PlayFreeSpin(BiggestBoyRoundState state, Func<int, int> random)
    {
        ValidateState(state);
        var (raw, stops) = Draw(random, state.Profile);
        var grid = raw.ToArray();
        var expanded = ImmutableArray.CreateBuilder<int>();
        for (var reel = 0; reel < ReelCount; reel++)
        {
            var offset = reel * Rows;
            if (!raw.Skip(offset).Take(Rows).Contains(state.ExpandingSymbol!.Value)) continue;
            expanded.Add(reel);
            for (var row = 0; row < Rows; row++) grid[offset + row] = state.ExpandingSymbol.Value;
        }
        var result = EvaluateGrid(state.LockedStake, grid);
        // Scatter triggers are counted before expansion can cover their visible cells.
        var scatters = raw.Count(x => x == BiggestBoySymbol.Scatter);
        var added = scatters >= 3 ? Math.Min(5, MaximumFreeSpinsAwarded - state.FreeSpinsAwarded) : 0;
        return result with
        {
            RawGrid = raw,
            Stops = stops,
            ScatterCount = scatters,
            ExpandedReels = expanded.ToImmutable(),
            State = state with
            {
                FreeSpinsRemaining = state.FreeSpinsRemaining - 1 + added,
                FreeSpinsAwarded = state.FreeSpinsAwarded + added,
                SpinsPlayed = state.SpinsPlayed + 1,
                TotalPayout = checked(state.TotalPayout + result.Payout),
                BonusPayout = checked(state.BonusPayout + result.Payout),
            },
        };
    }

    public static BiggestBoySpinResult EvaluateGrid(int stake, IReadOnlyList<BiggestBoySymbol> grid)
    {
        ValidateStake(stake);
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.Count != GridSize || grid.Any(x => !Enum.IsDefined(x))) throw new ArgumentException("Expected 15 valid symbols.", nameof(grid));
        var wins = ImmutableArray.CreateBuilder<BiggestBoyLineWin>();
        for (var line = 0; line < Paylines.Count; line++)
        {
            BiggestBoyLineWin? best = null;
            for (var candidate = BiggestBoySymbol.Kebap; candidate <= BiggestBoySymbol.T; candidate++)
            {
                var count = 0;
                foreach (var cell in Paylines[line])
                {
                    if (grid[cell] != candidate && grid[cell] != BiggestBoySymbol.Book) break;
                    count++;
                }
                if (count < 3) continue;
                var payout = checked(stake / PaylineCount * (long)Multiplier(candidate, count));
                if (best is null || payout > best.Payout) best = new(line, candidate, count, payout);
            }
            if (best is not null) wins.Add(best);
        }
        var total = wins.Sum(x => x.Payout);
        var immutable = grid.ToImmutableArray();
        return new(immutable, [], immutable.Count(x => x == BiggestBoySymbol.Scatter), [], wins.ToImmutable(), total, 0,
            new(stake, 0, 0, 0, total, Version, null), immutable);
    }

    private static (ImmutableArray<BiggestBoySymbol> Grid, ImmutableArray<int> Stops) Draw(Func<int, int> random, string profile)
    {
        ArgumentNullException.ThrowIfNull(random);
        // One server RNG decision per spin. At 100 every spin contains five O symbols on
        // payline 3 and three scatter symbols, giving a guaranteed line win + bonus trigger.
        // The result is still an actual five-stop physical strip result, not a payout override.
        var lucky = BiggestBoyProfiles.LuckyPercent(profile);
        if (lucky > 0 && Next(random, 100) < lucky)
        {
            int[] luckyStops = [50, 0, 49, 7, 4];
            // Both tail reels have fourteen valid O positions. Randomising their
            // stops keeps the guaranteed five-of-a-kind + 3-scatter pattern varied.
            for (var reel = 3; reel < ReelCount; reel++)
            {
                var choices = Enumerable.Range(0, StripLength)
                    .Where(stop => ReelStrips[reel][(stop + 2) % StripLength] == BiggestBoySymbol.O).ToArray();
                luckyStops[reel] = choices[Next(random, choices.Length)];
            }
            var boosted = ImmutableArray.CreateBuilder<BiggestBoySymbol>(GridSize);
            for (var reel = 0; reel < ReelCount; reel++)
                for (var row = 0; row < Rows; row++)
                    boosted.Add(ReelStrips[reel][(luckyStops[reel] + row) % StripLength]);
            return (boosted.ToImmutable(), luckyStops.ToImmutableArray());
        }
        var grid = ImmutableArray.CreateBuilder<BiggestBoySymbol>(GridSize);
        var stops = ImmutableArray.CreateBuilder<int>(ReelCount);
        for (var reel = 0; reel < ReelCount; reel++)
        {
            var stop = BiggestBoyProfiles.SelectStop(profile, reel, random); stops.Add(stop);
            for (var row = 0; row < Rows; row++) grid.Add(ReelStrips[reel][(stop + row) % StripLength]);
        }
        return (grid.ToImmutable(), stops.ToImmutable());
    }

    private static BiggestBoySymbol[] BuildStrip(int reel)
    {
        var regular = Counts.SelectMany((count, symbol) => Enumerable.Repeat((BiggestBoySymbol)symbol, count)).ToArray();
        var strip = new BiggestBoySymbol[StripLength];
        strip[0] = strip[50] = BiggestBoySymbol.Scatter;
        strip[25] = BiggestBoySymbol.Book;
        var index = 0;
        for (var position = 0; position < StripLength; position++)
            if (position is not (0 or 25 or 50)) strip[position] = regular[(index++ * 37 + reel * 19) % regular.Length];
        return strip;
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
        ArgumentNullException.ThrowIfNull(state); ValidateStake(state.LockedStake);
        BiggestBoyProfiles.Validate(state.Profile);
        if (state.RulesVersion != Version || state.ExpandingSymbol is not { } symbol || !IsRegular(symbol)
            || state.FreeSpinsRemaining is <= 0 or > MaximumFreeSpinsAwarded
            || state.FreeSpinsAwarded is < 10 or > MaximumFreeSpinsAwarded || state.FreeSpinsAwarded % 5 != 0
            || state.SpinsPlayed is < 1 or > MaximumFreeSpinsAwarded
            || state.FreeSpinsAwarded - state.FreeSpinsRemaining != state.SpinsPlayed - 1
            || state.TotalPayout < 0 || state.TotalPayout > MaximumRoundExposure(state.LockedStake)
            || state.BonusPayout < 0 || state.BonusPayout > state.TotalPayout)
            throw new ArgumentException("Invalid 5x3 expanding bonus state.", nameof(state));
    }
}
