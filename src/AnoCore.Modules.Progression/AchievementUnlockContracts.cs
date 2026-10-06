using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Progression;

public sealed record AchievementUnlockRecord(
    PlayerId PlayerId,
    string AchievementId,
    int Tier,
    int DefinitionVersion,
    long RewardXp,
    string GrantId,
    DateTimeOffset UnlockedAtUtc);

public sealed record AchievementTierUnlockCandidate(
    string AchievementId,
    int Tier,
    int DefinitionVersion,
    long RewardXp,
    string GrantId,
    DateTimeOffset OccurredAtUtc,
    long AwardedXp,
    string? BoostId,
    decimal BoostMultiplier);

public sealed record AchievementTierUnlockCommit(
    bool Applied,
    AchievementUnlockRecord Unlock,
    ProgressionGrantRecord RewardGrant);

public sealed record AchievementCommitResult(
    AchievementEvaluation Evaluation,
    IReadOnlyList<AchievementTierUnlockCommit> Unlocks);

public interface IAchievementUnlockRepository
{
    ValueTask<int> ReadHighestTierAsync(
        PlayerId playerId,
        string achievementId,
        CancellationToken cancellationToken = default);

    ValueTask<AchievementTierUnlockCommit> ApplyAsync(
        PlayerId playerId,
        AchievementTierUnlockCandidate candidate,
        CancellationToken cancellationToken = default);
}

public sealed class AchievementUnlockService
{
    private readonly IAchievementUnlockRepository _repository;
    private readonly ProgressionDefinitionSnapshot _progression;

    public AchievementUnlockService(
        IAchievementUnlockRepository repository,
        ProgressionDefinitionSnapshot progression)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
    }

    public async ValueTask<AchievementCommitResult> EvaluateAndCommitAsync(
        PlayerId playerId,
        AchievementDefinition definition,
        IEnumerable<GameplayStatTotal> totals,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(totals);

        var awardedTier = await _repository.ReadHighestTierAsync(
            playerId, definition.Id, cancellationToken).ConfigureAwait(false);
        var evaluation = definition.Evaluate(totals, awardedTier);
        if (evaluation.NewlyUnlocked.Count == 0)
            return new AchievementCommitResult(evaluation, Array.Empty<AchievementTierUnlockCommit>());

        var occurredAtUtc = NormalizeUtc(occurredAt);
        var committed = new List<AchievementTierUnlockCommit>(evaluation.NewlyUnlocked.Count);
        foreach (var tier in evaluation.NewlyUnlocked)
        {
            var boost = _progression.ResolveBoost(
                occurredAtUtc, ProgressionXpSource.AchievementReward);
            var awardedXp = _progression.ApplyBoost(
                tier.RewardXp, occurredAtUtc, ProgressionXpSource.AchievementReward);
            var grantId = GrantId(definition.Id, tier.Tier);

            committed.Add(await _repository.ApplyAsync(
                playerId,
                new AchievementTierUnlockCandidate(
                    definition.Id,
                    tier.Tier,
                    definition.Version,
                    tier.RewardXp,
                    grantId,
                    occurredAtUtc,
                    awardedXp,
                    boost.BoostId,
                    boost.Multiplier),
                cancellationToken).ConfigureAwait(false));
        }

        var highestCommittedTier = Math.Max(
            evaluation.UnlockedTier,
            committed.Count == 0 ? awardedTier : committed.Max(value => value.Unlock.Tier));
        var finalEvaluation = definition.Evaluate(totals, highestCommittedTier);
        return new AchievementCommitResult(
            finalEvaluation,
            committed.AsReadOnly());
    }

    public static string GrantId(string achievementId, int tier)
    {
        if (string.IsNullOrWhiteSpace(achievementId)
            || achievementId.Length > 64
            || achievementId.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException("Achievement ID is invalid.", nameof(achievementId));
        }
        if (tier is < 1 or > AchievementDefinition.MaxTiers)
            throw new ArgumentOutOfRangeException(nameof(tier));

        return $"achievement:{achievementId}:tier:{tier}";
    }

    private static DateTimeOffset NormalizeUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.UtcDateTime.Ticks;
        ticks -= ticks % TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
    }
}
