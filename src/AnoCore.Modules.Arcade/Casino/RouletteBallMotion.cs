namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Bounded presentation coordinates for an already booked European Roulette result.</summary>
public static class RouletteBallMotion
{
    public const int Angles = 148;
    public const int Rings = 6;
    public const int FrameCount = Angles * Rings;
    public const int LandingMilliseconds = 3600;
    public static IReadOnlyList<int> PocketOrder { get; } = Array.AsReadOnly(new[]
    {
        0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10,
        5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26,
    });

    public static int LandingFrame(int number)
    {
        if (number is < 0 or > 36) throw new ArgumentOutOfRangeException(nameof(number));
        var pocket = 0;
        while (PocketOrder[pocket] != number) pocket++;
        return (Rings - 1) * Angles + pocket * 4;
    }

    public static int FrameAt(int number, TimeSpan elapsed, int startAngle = 0)
    {
        var target = LandingFrame(number) % Angles;
        if (startAngle is < 0 or >= Angles) throw new ArgumentOutOfRangeException(nameof(startAngle));
        var progress = Math.Clamp((elapsed.TotalMilliseconds - 200) / 3400, 0, 1);
        if (progress >= 1) return LandingFrame(number);
        // Two full circuits plus the remaining arc, with continuously decreasing speed.
        var travel = 2 * Angles + (target - startAngle + Angles) % Angles;
        var angle = (startAngle + (int)Math.Round(travel * (1 - (1 - progress) * (1 - progress)))) % Angles;
        var ring = (int)Math.Round(Math.Clamp((progress - .7) / .3, 0, 1) * (Rings - 1));
        return ring * Angles + angle;
    }
}
