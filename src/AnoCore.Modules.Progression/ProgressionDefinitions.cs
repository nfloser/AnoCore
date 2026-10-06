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
    public const int MaxLevels = 500;
    public const int MaxBoosts = 128;
    public const decimal MaxBoostMultiplier = 10m;

    private const ProgressionXpSourceMask AllowedBoostSources =
        ProgressionXpSourceMask.Gameplay
        | ProgressionXpSourceMask.ChallengeReward
        | ProgressionXpSourceMask.AchievementReward;

    private readonly IReadOnlyList<XpLevelThreshold> _levels;
    private readonly IReadOnlyList<XpBoostDefinition> _boosts;

    private ProgressionDefinitionSnapshot(
        IReadOnlyList<XpLevelThreshold> levels,
        IReadOnlyList<XpBoostDefinition> boosts)
    {
        _levels = levels;
        _boosts = boosts;
    }

    public IReadOnlyList<XpLevelThreshold> Levels => _levels;

    public IReadOnlyList<XpBoostDefinition> Boosts => _boosts;

    public static ProgressionDefinitionSnapshot Create(
        IEnumerable<XpLevelThreshold> levels,
        IEnumerable<XpBoostDefinition>? boosts = null)
    {
        ArgumentNullException.ThrowIfNull(levels);

        var levelSnapshot = levels.ToArray();
        if (levelSnapshot.Length is < 1 or > MaxLevels)
            throw new ArgumentException(
                $"Define between 1 and {MaxLevels} progression levels.",
                nameof(levels));

        long previousMinimum = -1;
        for (var index = 0; index < levelSnapshot.Length; index++)
        {
            var level = levelSnapshot[index];
            if (level is null)
                throw new ArgumentException("Progression levels cannot contain null entries.", nameof(levels));

            var expectedLevel = index + 1;
            if (level.Level != expectedLevel)
                throw new ArgumentException(
                    "Progression levels must be consecutive and start at level 1.",
                    nameof(levels));
            if (index == 0 && level.MinimumXp != 0)
                throw new ArgumentException(
                    "Progression level 1 must start at zero XP.",
                    nameof(levels));
            if (level.MinimumXp < 0 || level.MinimumXp <= previousMinimum)
                throw new ArgumentException(
                    "Progression level XP thresholds must be nonnegative and strictly increasing.",
                    nameof(levels));

            previousMinimum = level.MinimumXp;
        }

        var boostSnapshot = (boosts ?? []).ToArray();
        if (boostSnapshot.Length > MaxBoosts)
            throw new ArgumentException(
                $"Define at most {MaxBoosts} XP boost windows.",
                nameof(boosts));

        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var boost in boostSnapshot)
        {
            if (boost is null)
                throw new ArgumentException("XP boost definitions cannot contain null entries.", nameof(boosts));
            if (!PrintableIdentifier(boost.Id))
                throw new ArgumentException(
                    "XP boost IDs must contain 1-64 printable characters without surrounding whitespace.",
                    nameof(boosts));
            if (!identifiers.Add(boost.Id))
                throw new ArgumentException("XP boost IDs must be unique.", nameof(boosts));
            if (boost.StartsAtUtc.Offset != TimeSpan.Zero || boost.EndsAtUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("XP boost windows must be defined in UTC.", nameof(boosts));
            if (boost.StartsAtUtc >= boost.EndsAtUtc)
                throw new ArgumentException(
                    "XP boost windows use [start, end) semantics and require start before end.",
                    nameof(boosts));
            if (boost.Multiplier is < 1m or > MaxBoostMultiplier)
                throw new ArgumentException(
                    $"XP boost multipliers must be between 1 and {MaxBoostMultiplier}.",
                    nameof(boosts));
            if (boost.EligibleSources == ProgressionXpSourceMask.None
                || (boost.EligibleSources & ~AllowedBoostSources) != 0)
            {
                throw new ArgumentException(
                    "XP boost eligible sources must contain only gameplay, challenge reward or achievement reward.",
                    nameof(boosts));
            }
        }

        var orderedBoosts = boostSnapshot
            .OrderBy(value => value.StartsAtUtc)
            .ThenBy(value => value.Id, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

        return new ProgressionDefinitionSnapshot(
            levelSnapshot.ToList().AsReadOnly(),
            orderedBoosts);
    }

    public XpLevelThreshold LevelFor(long xp)
    {
        if (xp < 0) throw new ArgumentOutOfRangeException(nameof(xp));

        var lower = 0;
        var upper = _levels.Count - 1;
        while (lower <= upper)
        {
            var middle = lower + ((upper - lower) / 2);
            if (_levels[middle].MinimumXp <= xp)
                lower = middle + 1;
            else
                upper = middle - 1;
        }

        return _levels[upper];
    }

    public XpBoostResolution ResolveBoost(
        DateTimeOffset at,
        ProgressionXpSource source)
    {
        var sourceMask = SourceMask(source);
        if (sourceMask == ProgressionXpSourceMask.None)
            return new XpBoostResolution(null, 1m);

        var instant = at.ToUniversalTime();
        XpBoostDefinition? selected = null;
        foreach (var boost in _boosts)
        {
            if (boost.StartsAtUtc > instant)
                break;
            if (instant >= boost.EndsAtUtc
                || (boost.EligibleSources & sourceMask) == 0)
            {
                continue;
            }

            if (selected is null
                || boost.Multiplier > selected.Multiplier
                || boost.Multiplier == selected.Multiplier
                    && string.CompareOrdinal(boost.Id, selected.Id) < 0)
            {
                selected = boost;
            }
        }

        return selected is null
            ? new XpBoostResolution(null, 1m)
            : new XpBoostResolution(selected.Id, selected.Multiplier);
    }

    public long ApplyBoost(
        long baseXp,
        DateTimeOffset at,
        ProgressionXpSource source)
    {
        if (baseXp < 0) throw new ArgumentOutOfRangeException(nameof(baseXp));

        var multiplier = ResolveBoost(at, source).Multiplier;
        var scaled = decimal.Truncate(baseXp * multiplier);
        return checked((long)scaled);
    }

    private static ProgressionXpSourceMask SourceMask(ProgressionXpSource source)
        => source switch
        {
            ProgressionXpSource.Gameplay => ProgressionXpSourceMask.Gameplay,
            ProgressionXpSource.ChallengeReward => ProgressionXpSourceMask.ChallengeReward,
            ProgressionXpSource.AchievementReward => ProgressionXpSourceMask.AchievementReward,
            ProgressionXpSource.Administration => ProgressionXpSourceMask.None,
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

    private static bool PrintableIdentifier(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 64
            && value == value.Trim()
            && value.All(character => !char.IsControl(character));
}
