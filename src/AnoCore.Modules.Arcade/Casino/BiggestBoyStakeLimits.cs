namespace AnoCore.Modules.Arcade.Casino;

/// <summary>New controlled-round stakes; historical game-rule limits remain frozen.</summary>
public static class BiggestBoyStakeLimits
{
    public const int Maximum = 100_000;

    public static bool IsValid(int stake) => stake is >= 50 and <= Maximum && stake % 50 == 0;

    public static int Clamp(int current, int maximum) => Math.Clamp(current, 50, maximum);

    public static int Step(int current, int direction, int maximum)
        => new CasinoStakeLimits { Book = maximum }.StepBookStake(current, direction);
}
