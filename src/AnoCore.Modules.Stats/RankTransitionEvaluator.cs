namespace AnoCore.Modules.Stats;

public enum RankTransitionKind
{
    Promotion,
    Demotion,
}

public sealed record RankTransition(
    RankTransitionKind Kind,
    RankThreshold Previous,
    RankThreshold Current,
    long PreviousPoints,
    long CurrentPoints);

public static class RankTransitionEvaluator
{
    public static RankTransition? Evaluate(
        RankConfiguration configuration, long previousPoints, long currentPoints)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (previousPoints < 0) throw new ArgumentOutOfRangeException(nameof(previousPoints));
        if (currentPoints < 0) throw new ArgumentOutOfRangeException(nameof(currentPoints));
        var previous = configuration.ForScore(previousPoints);
        var current = configuration.ForScore(currentPoints);
        if (previous == current) return null;
        return new RankTransition(
            current.MinimumPoints > previous.MinimumPoints
                ? RankTransitionKind.Promotion
                : RankTransitionKind.Demotion,
            previous, current, previousPoints, currentPoints);
    }
}
