namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Finite native travel on the same cyclic strips as the booked result.</summary>
public static class BiggestBoyReelMotion
{
    public const int BaseOffset = 200;
    public const int RenderedCells = 303;
    public const double TotalDurationSeconds = 3.9;
    public const double FirstReelDurationSeconds = 1.9;
    public const double StopIntervalSeconds = .45;
    public const int MinimumSteps = 30;
    public const int MaximumSteps = 129;
    public static int Steps(int previousStop, int bookedStop)
    {
        if (previousStop is < 0 or >= 100 || bookedStop is < 0 or >= 100) throw new ArgumentOutOfRangeException(nameof(bookedStop));
        var distance = (previousStop - bookedStop + 100) % 100;
        return distance < MinimumSteps ? distance + 100 : distance;
    }
    public static int FinalVisibleIndex(int previousStop, int steps) => BaseOffset + previousStop - steps;
    /// <summary>Small, bounded display window into the authoritative 100-stop cyclic strip.</summary>
    public static int VisibleIndex(int previousStop, int travelled, int row)
    {
        if (previousStop is < 0 or >= 100) throw new ArgumentOutOfRangeException(nameof(previousStop));
        if (travelled is < 0 or > MaximumSteps) throw new ArgumentOutOfRangeException(nameof(travelled));
        if (row is < 0 or >= 4) throw new ArgumentOutOfRangeException(nameof(row));
        return ((previousStop - travelled + row) % 100 + 100) % 100;
    }
    /// <summary>Advance along booked physical stops, without deriving outcome from display timing.</summary>
    public static int Travelled(int previousStop, int bookedStop, double elapsedSeconds, int reel)
    {
        var steps = Steps(previousStop, bookedStop);
        var duration = Duration(reel);
        if (double.IsNaN(elapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        return elapsedSeconds <= 0 ? 0 : elapsedSeconds >= duration
            ? steps : Math.Clamp((int)Math.Floor(elapsedSeconds * steps / duration), 0, steps);
    }
    public static double Duration(int reel)
    {
        if (reel is < 0 or >= 5) throw new ArgumentOutOfRangeException(nameof(reel));
        return FirstReelDurationSeconds + reel * StopIntervalSeconds;
    }
}

/// <summary>Cosmetic eight-symbol loop for Book; the durable booked grid remains authoritative.</summary>
public static class BookReelMotion
{
    public static int Travelled(double elapsedSeconds, int reel)
    {
        _ = BiggestBoyReelMotion.Duration(reel); // validate the reel index
        if (double.IsNaN(elapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        return Math.Clamp((int)Math.Floor(Math.Max(0, elapsedSeconds) * 10), 0, 72);
    }

    public static BonusSlotSymbol VisibleSymbol(int reel, int travelled, int row)
    {
        if (reel is < 0 or >= 5 || row is < 0 or >= 4 || travelled is < 0 or > 72)
            throw new ArgumentOutOfRangeException(nameof(reel));
        return (BonusSlotSymbol)((reel + travelled + row) % 8);
    }
}
