using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression;

public sealed record SeasonLeaderboardEntry(long Placement, PlayerId PlayerId, long SeasonXp);

public interface ISeasonRewardRepository
{
    // Returns newly applied grants; each grant is independently atomic and retryable.
    ValueTask<int> ReconcileAsync(PersistedSeason season, int batchSize, DateTimeOffset at,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<SeasonLeaderboardEntry>> ReadTopAsync(string seasonId, int seasonVersion,
        int offset, int limit, CancellationToken cancellationToken = default);
}
