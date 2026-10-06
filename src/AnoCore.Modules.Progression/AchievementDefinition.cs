using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Progression;

public sealed record AchievementTier(int Tier, long Target, long RewardXp);

public sealed record AchievementEvaluation(long Count, int UnlockedTier,
    long? NextTarget, IReadOnlyList<AchievementTier> NewlyUnlocked);

public sealed class AchievementDefinition
{
    public const int MaxTiers = 100;

    private AchievementDefinition(string id, int version, GameplayStatKind statistic,
        IReadOnlyList<AchievementTier> tiers)
    {
        Id = id;
        Version = version;
        Statistic = statistic;
        Tiers = tiers;
    }

    public string Id { get; }
    public int Version { get; }
    public GameplayStatKind Statistic { get; }
    public IReadOnlyList<AchievementTier> Tiers { get; }

    public static AchievementDefinition Create(string id, int version,
        GameplayStatKind statistic, IEnumerable<AchievementTier> tiers)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64
            || id.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
            throw new ArgumentException("Achievement IDs require 1-64 ASCII letters, digits, dots, dashes or underscores.", nameof(id));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        if (!Enum.IsDefined(statistic)) throw new ArgumentOutOfRangeException(nameof(statistic));
        ArgumentNullException.ThrowIfNull(tiers);
        var snapshot = tiers.Take(MaxTiers + 1).ToArray();
        if (snapshot.Length is < 1 or > MaxTiers)
            throw new ArgumentException("Define between 1 and 100 achievement tiers.", nameof(tiers));
        long previousTarget = 0;
        for (var index = 0; index < snapshot.Length; index++)
        {
            var tier = snapshot[index];
            if (tier is null || tier.Tier != index + 1 || tier.Target <= previousTarget || tier.RewardXp < 0)
                throw new ArgumentException("Tiers must be consecutive with increasing positive targets and nonnegative XP rewards.", nameof(tiers));
            previousTarget = tier.Target;
        }
        return new AchievementDefinition(id, version, statistic, Array.AsReadOnly(snapshot));
    }

    public AchievementEvaluation Evaluate(IEnumerable<GameplayStatTotal> totals, int awardedTier)
    {
        ArgumentNullException.ThrowIfNull(totals);
        if (awardedTier < 0 || awardedTier > Tiers.Count)
            throw new ArgumentOutOfRangeException(nameof(awardedTier));
        var seen = new HashSet<GameplayStatKind>();
        var limit = Enum.GetValues<GameplayStatKind>().Length;
        long count = 0;
        foreach (var total in totals.Take(limit + 1))
        {
            if (total is null || !Enum.IsDefined(total.Kind) || total.Count < 0 || !seen.Add(total.Kind))
                throw new ArgumentException("Statistic totals must have unique known kinds and nonnegative counts.", nameof(totals));
            if (total.Kind == Statistic) count = total.Count;
        }
        var unlocked = awardedTier;
        var newlyUnlocked = new List<AchievementTier>();
        foreach (var tier in Tiers)
        {
            if (tier.Target > count) break;
            unlocked = Math.Max(unlocked, tier.Tier);
            if (tier.Tier > awardedTier) newlyUnlocked.Add(tier);
        }
        return new AchievementEvaluation(count, unlocked,
            unlocked < Tiers.Count ? Tiers[unlocked].Target : null, newlyUnlocked.AsReadOnly());
    }
}
