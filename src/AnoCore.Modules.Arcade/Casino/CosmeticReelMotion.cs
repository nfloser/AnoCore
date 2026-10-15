namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Per-animation cosmetic ribbons. Their randomness never selects or changes a paid outcome.</summary>
public sealed class CosmeticReelMotion
{
    private const double LandingSeconds = .35;
    private readonly int[][] _ribbons;

    private CosmeticReelMotion(int[][] ribbons) => _ribbons = ribbons;

    /// <summary>Build shuffled symbol bags independently of the booked field, then attach its landing cells.</summary>
    public static CosmeticReelMotion Create(int symbolCount, IReadOnlyList<int> bookedGrid, Func<int, int> random)
    {
        ArgumentNullException.ThrowIfNull(bookedGrid);
        ArgumentNullException.ThrowIfNull(random);
        if (symbolCount is < 2 or > 12 || bookedGrid.Count != 15 || bookedGrid.Any(x => x < 0 || x >= symbolCount))
            throw new ArgumentException("Expected five three-cell reels with valid symbols.", nameof(bookedGrid));
        var ribbons = new int[5][];
        for (var reel = 0; reel < 5; reel++)
        {
            var ribbon = new int[Steps(reel) + 4];
            var bag = Enumerable.Range(0, symbolCount).ToArray();
            for (var offset = 0; offset < ribbon.Length; offset += symbolCount)
            {
                for (var i = bag.Length - 1; i > 0; i--)
                {
                    var j = random(i + 1);
                    if (j < 0 || j > i) throw new ArgumentException("Invalid cosmetic random draw.", nameof(random));
                    (bag[i], bag[j]) = (bag[j], bag[i]);
                }
                Array.Copy(bag, 0, ribbon, offset, Math.Min(symbolCount, ribbon.Length - offset));
            }
            // The scrolling window moves downward toward index zero. Only the final
            // three steps introduce booked symbols, rather than repeating them all spin.
            for (var row = 0; row < 3; row++) ribbon[row] = bookedGrid[reel * 3 + row];
            ribbons[reel] = ribbon;
        }
        return new(ribbons);
    }

    public static int Steps(int reel) => (int)Math.Floor((BiggestBoyReelMotion.Duration(reel) - LandingSeconds) * 18) + 3;

    /// <summary>Regular row advances followed by three slower landing steps at the original stop time.</summary>
    public static int Travelled(double elapsedSeconds, int reel)
    {
        var duration = BiggestBoyReelMotion.Duration(reel);
        if (double.IsNaN(elapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        var steps = Steps(reel);
        if (elapsedSeconds <= 0) return 0;
        if (elapsedSeconds >= duration) return steps;
        var landingAt = duration - LandingSeconds;
        return elapsedSeconds < landingAt
            ? (int)Math.Floor(elapsedSeconds / landingAt * (steps - 3))
            : steps - 3 + (int)Math.Floor((elapsedSeconds - landingAt) / LandingSeconds * 3);
    }

    public int VisibleSymbol(int reel, int travelled, int row)
    {
        var steps = Steps(reel);
        if (travelled < 0 || travelled > steps || row is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(travelled));
        return _ribbons[reel][steps - travelled + row];
    }
}
