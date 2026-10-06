using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Progression;

public sealed record AchievementUnlockRecord(string AchievementId, int DefinitionVersion,
    int Tier, ProgressionGrantRecord Grant);

public interface IAchievementRepository
{
    ValueTask<int> ReadAwardedTierAsync(PlayerId playerId, string achievementId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AchievementUnlockRecord>> UnlockAsync(PlayerId playerId,
        AchievementDefinition definition, IEnumerable<GameplayStatTotal> totals,
        DateTimeOffset occurredAt, ProgressionDefinitionSnapshot xpDefinitions,
        CancellationToken cancellationToken = default);
}
