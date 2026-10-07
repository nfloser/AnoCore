using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Progression;

public sealed record AchievementPrerequisite(string AchievementId, int Tier);

public sealed record AchievementTier(int Tier, long Target, long RewardXp);

public sealed record AchievementEvaluation(long Count, int UnlockedTier,
    long? NextTarget, IReadOnlyList<AchievementTier> NewlyUnlocked);

public sealed class AchievementDefinition
{
    public const int MaxTiers = 100;
    public const int MaxPrerequisites = 32;

    private AchievementDefinition(string id, int version, GameplayStatKind statistic,
        IReadOnlyList<AchievementTier> tiers, IReadOnlyList<AchievementPrerequisite> prerequisites)
    {
        Id = id;
        Version = version;
        Statistic = statistic;
        Tiers = tiers;
        Prerequisites = prerequisites;
    }

    public string Id { get; }
    public int Version { get; }
    public GameplayStatKind Statistic { get; }
    public IReadOnlyList<AchievementTier> Tiers { get; }

    public IReadOnlyList<AchievementPrerequisite> Prerequisites { get; }

    public static AchievementDefinition Create(string id, int version,
        GameplayStatKind statistic, IEnumerable<AchievementTier> tiers)
        => Create(id, version, statistic, tiers, []);

    public static AchievementDefinition Create(string id, int version,
        GameplayStatKind statistic, IEnumerable<AchievementTier> tiers,
        IEnumerable<AchievementPrerequisite>? prerequisites)
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
        var requirements = (prerequisites ?? []).Take(MaxPrerequisites + 1).ToArray();
        if (requirements.Length > MaxPrerequisites)
            throw new ArgumentException("Define at most 32 achievement prerequisites.", nameof(prerequisites));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requirement in requirements)
        {
            if (requirement is null || string.IsNullOrWhiteSpace(requirement.AchievementId)
                || requirement.AchievementId.Length > 64
                || requirement.AchievementId.Any(character => !char.IsAsciiLetterOrDigit(character)
                    && character is not '-' and not '_' and not '.')
                || requirement.Tier is < 1 or > MaxTiers
                || requirement.AchievementId == id || !ids.Add(requirement.AchievementId))
                throw new ArgumentException("Achievement prerequisites require unique valid IDs, positive bounded tiers and no self-reference.", nameof(prerequisites));
        }
        return new AchievementDefinition(id, version, statistic, Array.AsReadOnly(snapshot), Array.AsReadOnly(requirements));
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
