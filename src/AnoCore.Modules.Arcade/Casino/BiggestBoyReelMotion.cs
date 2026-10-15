namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Finite native travel on the same cyclic strips as the booked result.</summary>
public static class BiggestBoyReelMotion
{
    public const int BaseOffset = 200;
    public const int RenderedCells = 303;
    public const int MinimumSteps = 30;
    public const int MaximumSteps = 129;
    public static int Steps(int previousStop, int bookedStop)
    {
        if (previousStop is < 0 or >= 100 || bookedStop is < 0 or >= 100) throw new ArgumentOutOfRangeException(nameof(bookedStop));
        var distance = (previousStop - bookedStop + 100) % 100;
        return distance < MinimumSteps ? distance + 100 : distance;
    }
    public static int FinalVisibleIndex(int previousStop, int steps) => BaseOffset + previousStop - steps;
    public static double Duration(int reel)
    {
        if (reel is < 0 or >= 5) throw new ArgumentOutOfRangeException(nameof(reel));
        return 5.2 + reel * .2;
    }
}
