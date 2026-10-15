namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Exact independent per-spin outcome probabilities, frozen with the accepted round.</summary>
public sealed record SlotOutcomeSettings(int WinPercent = 25, int FreeSpinPercent = 1, int FullScreenPercent = 1)
{
    public SlotOutcomeSettings Validate()
    {
        if (WinPercent is < 0 or > 100 || FreeSpinPercent is < 0 or > 100 || FullScreenPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(WinPercent), "Slot percentages must be 0..100.");
        return this;
    }
}
