using System.Collections.Immutable;

namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Versioned virtual reels with independently drawn payout and bonus events.
/// Evaluators and exposure limits are shared with the immutable legacy paytables.</summary>
public static class ControlledSlotRules
{
    public const string LegacyBiggestVersion = "anocore-biggest-boy-controlled-v5";
    public const string BiggestVersion = "anocore-biggest-boy-controlled-v6";
    public const string LegacyBookVersion = "anocore-book-controlled-v2";
    public const string BookVersion = "anocore-book-controlled-v3";
    public static bool IsBiggest(string version) => version is BiggestVersion or LegacyBiggestVersion or BiggestBoyExpandingRules.Version;

    public static bool IsControlledBiggest(string? version) => version is BiggestVersion or LegacyBiggestVersion;
    public static bool IsControlledBook(string? version) => version is BookVersion or LegacyBookVersion;

    public static BiggestBoySpinResult Biggest(int stake, Func<int, int> random, SlotOutcomeSettings settings,
        BiggestBoyRoundState? previous = null)
    {
        settings = (previous?.OutcomeSettings ?? settings).Validate();
        var version = previous?.RulesVersion ?? BiggestVersion;
        if (!IsControlledBiggest(version)) throw new ArgumentException("Invalid controlled slot version.");
        ValidatePrevious(previous?.FreeSpinsRemaining, previous?.RulesVersion, version);
        Func<int, IReadOnlyList<BiggestBoySymbol>, BiggestBoySpinResult> evaluate = version == LegacyBiggestVersion
            ? BiggestBoyExpandingRules.EvaluateGrid : BiggestBoyBalancedRules.EvaluateGrid;
        if (previous is not null && (previous.LockedStake != stake || previous.FreeSpinsAwarded is < 10 or > 30
            || previous.SpinsPlayed is < 1 or > 30 || previous.FreeSpinsRemaining > previous.FreeSpinsAwarded
            || previous.FreeSpinsAwarded - previous.FreeSpinsRemaining != previous.SpinsPlayed - 1
            || previous.TotalPayout < 0 || previous.ExpandingSymbol is null))
            throw new ArgumentException("Inconsistent controlled slot snapshot.", nameof(previous));
        var bonus = Pick(random, 100) < settings.FreeSpinPercent;
        var win = Pick(random, 100) < settings.WinPercent;
        var full = Pick(random, 100) < settings.FullScreenPercent && win && !bonus;
        var raw = Draw(random, 10, bonus, full, win, grid =>
        {
            var expanded = Expand(grid, previous?.ExpandingSymbol is { } symbol ? (int)symbol : null);
            return evaluate(stake, expanded.Select(x => (BiggestBoySymbol)x).ToArray()).Payout;
        }, previous is null ? stake : 0, previous?.ExpandingSymbol is { } selected ? (int)selected : null, wild: true, balanced: version == BiggestVersion);
        var expandedGrid = Expand(raw, previous?.ExpandingSymbol is { } expanding ? (int)expanding : null);
        var result = evaluate(stake, expandedGrid.Select(x => (BiggestBoySymbol)x).ToArray());
        var awarded = previous?.FreeSpinsAwarded ?? 0;
        var scatterCount = raw.Count(x => x == 10);
        var added = bonus ? previous is null ? BiggestBoyExpandingRules.FreeSpinsForScatters(scatterCount) : Math.Min(5, 30 - awarded) : 0;
        var state = new BiggestBoyRoundState(stake, (previous?.FreeSpinsRemaining ?? 1) - 1 + added,
            awarded + added, (previous?.SpinsPlayed ?? 0) + 1, checked((previous?.TotalPayout ?? 0) + result.Payout),
            version, previous?.ExpandingSymbol ?? (added > 0 ? (BiggestBoySymbol?)Pick(random, 10) : null),
            BonusPayout: checked((previous?.BonusPayout ?? 0) + (previous is null ? 0 : result.Payout)), OutcomeSettings: settings);
        return result with
        {
            State = state,
            ChargedStake = previous is null ? stake : 0,
            RawGrid = raw.Select(x => (BiggestBoySymbol)x).ToImmutableArray(),
            ScatterCount = scatterCount,
            Stops = ImmutableArray.Create(0, 0, 0, 0, 0),
            ExpandedReels = Enumerable.Range(0, 5).Where(r => !raw.Skip(r * 3).Take(3).SequenceEqual(expandedGrid.Skip(r * 3).Take(3))).ToImmutableArray()
        };
    }

    public static BonusSlotSpinResult Book(int stake, Func<int, int> random, SlotOutcomeSettings settings,
        BonusSlotRoundState? previous = null)
    {
        settings = (previous?.OutcomeSettings ?? settings).Validate();
        var version = previous?.RulesVersion ?? BookVersion;
        if (!IsControlledBook(version)) throw new ArgumentException("Invalid controlled slot version.");
        ValidatePrevious(previous?.FreeSpinsRemaining, previous?.RulesVersion, version);
        Func<int, IReadOnlyList<BonusSlotSymbol>, BonusSlotSpinResult> evaluate = version == LegacyBookVersion
            ? BonusSlotRules.EvaluateGrid : BookBalancedRules.EvaluateGrid;
        if (previous is not null && (previous.LockedStake != stake || previous.FreeSpinsAwarded is < 10 or > 30
            || previous.SpinsPlayed is < 1 or > 30 || previous.FreeSpinsRemaining > previous.FreeSpinsAwarded
            || previous.FreeSpinsAwarded - previous.FreeSpinsRemaining != previous.SpinsPlayed - 1
            || previous.TotalPayout < 0 || previous.ExpandingSymbol is null))
            throw new ArgumentException("Inconsistent controlled slot snapshot.", nameof(previous));
        var bonus = Pick(random, 100) < settings.FreeSpinPercent;
        var win = Pick(random, 100) < settings.WinPercent;
        var full = Pick(random, 100) < settings.FullScreenPercent && win && !bonus;
        var raw = Draw(random, 7, bonus, full, win, grid =>
            evaluate(stake, Expand(grid, previous?.ExpandingSymbol is { } s ? (int)s : null)
                .Select(x => (BonusSlotSymbol)x).ToArray()).Payout,
            previous is null ? stake : 0, previous?.ExpandingSymbol is { } symbol ? (int)symbol : null, balanced: version == BookVersion);
        var scatterCount = raw.Count(x => x == 7);
        var grid = Expand(raw, previous?.ExpandingSymbol is { } selected ? (int)selected : null);
        var result = evaluate(stake, grid.Select(x => (BonusSlotSymbol)x).ToArray());
        var awarded = previous?.FreeSpinsAwarded ?? 0;
        var remaining = previous?.FreeSpinsRemaining ?? 1;
        var added = bonus ? previous is null ? 10 : Math.Min(5, Math.Min(30 - awarded, 21 - remaining)) : 0;
        return result with
        {
            ScatterCount = scatterCount,
            ChargedStake = previous is null ? stake : 0,
            State = new(stake, remaining - 1 + added, awarded + added, (previous?.SpinsPlayed ?? 0) + 1,
                checked((previous?.TotalPayout ?? 0) + result.Payout),
                previous?.ExpandingSymbol ?? (added > 0 ? (BonusSlotSymbol?)Pick(random, 7) : null), version, settings)
        };
    }

    private static int[] Draw(Func<int, int> random, int symbols, bool bonus, bool full, bool win,
        Func<int[], long> payout, long threshold, int? expanding, bool wild = false, bool balanced = false)
    {
        // Bounded rejection keeps natural-looking varied fields. A constructed field
        // guarantees termination even for an adversarial deterministic RNG in tests.
        var scatterCount = bonus ? 3 + Pick(random, 3) : full ? 0 : Pick(random, 3);
        for (var attempt = 0; attempt < 128; attempt++)
        {
            var grid = Enumerable.Range(0, 15).Select(_ => wild && Pick(random, 100) < 2 ? 11 : Pick(random, symbols)).ToArray();
            if (full) Array.Fill(grid, Pick(random, symbols));
            var reels = new[] { 0, 1, 2, 3, 4 };
            for (var i = 0; i < scatterCount; i++)
            {
                var chosenReel = i + Pick(random, 5 - i);
                (reels[i], reels[chosenReel]) = (reels[chosenReel], reels[i]);
                grid[reels[i] * 3 + Pick(random, 3)] = symbols;
            }
            if ((payout(grid) > threshold) == win) return grid;
        }
        var chosen = Pick(random, symbols);
        if (!win && expanding == chosen) chosen = (chosen + 1) % symbols;
        if (balanced && win && !full)
        {
            // A bounded fallback must not turn an ordinary win into a forced full screen.
            // Try a single winning line against a varied background, including bonus scatters.
            for (var symbolOffset = 0; symbolOffset < symbols; symbolOffset++)
                for (var count = 3; count <= 5; count++)
                {
                    var symbol = (chosen + symbolOffset) % symbols;
                    var sparse = Enumerable.Range(0, 15).Select(cell => (symbol + 1 + cell / 3) % symbols).ToArray();
                    foreach (var cell in BonusSlotRules.Paylines[0].Take(count)) sparse[cell] = symbol;
                    for (var pattern = 0; pattern < (int)Math.Pow(3, scatterCount); pattern++)
                    {
                        var candidate = (int[])sparse.Clone();
                        var rows = pattern;
                        for (var reel = 0; reel < scatterCount; reel++)
                        {
                            candidate[reel * 3 + rows % 3] = symbols;
                            rows /= 3;
                        }
                        if (payout(candidate) > threshold) return candidate;
                    }
                }
            throw new InvalidOperationException("Unable to construct a regular winning line.");
        }
        var fallback = Enumerable.Range(0, 15).Select(cell => win ? chosen :
            (chosen + cell / 3) % symbols).ToArray();
        if (!win && expanding is { } excluded)
            for (var i = 0; i < 15; i++) if (fallback[i] == excluded) fallback[i] = (excluded + 1) % symbols;
        // Search the finite 3^5 scatter-row arrangements, never an unbounded RNG retry.
        for (var pattern = 0; pattern < 243; pattern++)
        {
            var candidate = (int[])fallback.Clone();
            var rows = pattern;
            for (var reel = 0; reel < scatterCount; reel++)
            {
                candidate[reel * 3 + rows % 3] = symbols;
                rows /= 3;
            }
            if ((payout(candidate) > threshold) == win) return candidate;
        }
        throw new InvalidOperationException("Unable to construct the selected slot outcome.");
    }

    private static int[] Expand(int[] raw, int? selected)
    {
        var grid = (int[])raw.Clone();
        if (selected is not { } symbol) return grid;
        for (var reel = 0; reel < 5; reel++)
            if (raw.Skip(reel * 3).Take(3).Contains(symbol))
                Array.Fill(grid, symbol, reel * 3, 3);
        return grid;
    }

    private static void ValidatePrevious(int? remaining, string? version, string expected)
    {
        if (remaining is not null && (remaining <= 0 || version != expected))
            throw new ArgumentException("Invalid controlled slot state.");
    }

    private static int Pick(Func<int, int> random, int bound)
    {
        var value = random(bound);
        return value >= 0 && value < bound ? value : throw new ArgumentException("Invalid random draw.", nameof(random));
    }
}
