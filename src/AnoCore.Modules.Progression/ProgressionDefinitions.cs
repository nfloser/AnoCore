namespace AnoCore.Modules.Progression;

public enum ProgressionXpSource
{
    Gameplay,
    ChallengeReward,
    AchievementReward,
    Administration,
}

[Flags]
public enum ProgressionXpSourceMask
{
    None = 0,
    Gameplay = 1,
    ChallengeReward = 2,
    AchievementReward = 4,
}

public sealed record XpLevelThreshold(int Level, long MinimumXp);

public sealed record XpBoostDefinition(
    string Id,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    decimal Multiplier,
    ProgressionXpSourceMask EligibleSources = ProgressionXpSourceMask.Gameplay);

public sealed record XpBoostResolution(string? BoostId, decimal Multiplier);

public sealed class ProgressionDefinitionSnapshot
{
    public IReadOnlyList<XpLevelThreshold> Levels
        => throw new NotImplementedException();

    public IReadOnlyList<XpBoostDefinition> Boosts
        => throw new NotImplementedException();

    public static ProgressionDefinitionSnapshot Create(
        IEnumerable<XpLevelThreshold> levels,
        IEnumerable<XpBoostDefinition>? boosts = null)
        => throw new NotImplementedException();

    public XpLevelThreshold LevelFor(long xp)
        => throw new NotImplementedException();

    public XpBoostResolution ResolveBoost(
        DateTimeOffset at,
        ProgressionXpSource source)
        => throw new NotImplementedException();

    public long ApplyBoost(
        long baseXp,
        DateTimeOffset at,
        ProgressionXpSource source)
        => throw new NotImplementedException();
}
