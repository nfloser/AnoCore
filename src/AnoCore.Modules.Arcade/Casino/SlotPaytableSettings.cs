namespace AnoCore.Modules.Arcade.Casino;

/// <summary>
/// Optional per-game controlled-slot paytable. Values are gross multipliers of
/// the stake assigned to one payline; absent rows retain the versioned defaults.
/// Once a round starts this object is stored in its durable snapshot.
/// </summary>
public sealed record SlotPaytableSettings(int LineStakeDivisor = 10, int[][]? Multipliers = null)
{
    public SlotPaytableSettings Validate(int expectedSymbols)
    {
        // Every legal casino stake is a multiple of 50; do not truncate line stake.
        if (LineStakeDivisor is not (1 or 2 or 5 or 10 or 25 or 50))
            throw new ArgumentOutOfRangeException(nameof(LineStakeDivisor),
                "LineStakeDivisor must be 1, 2, 5, 10, 25 or 50.");
        if (Multipliers is null) return this;
        if (Multipliers.Length != expectedSymbols
            || Multipliers.Any(row => row is null || row.Length != 3
                || row.Any(value => value is < 1 or > 10_000)))
            throw new ArgumentException(
                $"Multipliers must contain {expectedSymbols} rows of three values (1..10000) for 3/4/5 matches.",
                nameof(Multipliers));
        return this;
    }

    public int Multiplier(int symbol, int count, int defaultMultiplier) =>
        Multipliers is null ? defaultMultiplier : Multipliers[symbol][count - 3];

    /// <summary>Conservative reservation covering ten lines and 31 paid/bonus spins.</summary>
    public long MaximumRoundExposure(int stake, int defaultMaximum)
    {
        if (stake < 50 || stake % LineStakeDivisor != 0)
            throw new ArgumentOutOfRangeException(nameof(stake));
        var highest = Multipliers is null ? defaultMaximum : Multipliers.SelectMany(row => row).Max();
        return checked((long)(stake / LineStakeDivisor) * highest * BonusSlotRules.PaylineCount
            * (BiggestBoyExpandingRules.MaximumFreeSpinsAwarded + 1L));
    }
}
