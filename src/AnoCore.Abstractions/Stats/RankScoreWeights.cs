using System.Collections.Frozen;
using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Stats;

public sealed class RankScoreWeights
{
    public RankScoreWeights(int killPoints, int assistPoints, int deathPenalty,
        IReadOnlyDictionary<GameplayStatKind, int>? gameplayPoints = null)
    {
        if (killPoints is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(killPoints));
        if (assistPoints is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(assistPoints));
        if (deathPenalty is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(deathPenalty));
        if (gameplayPoints is not null && gameplayPoints.Any(pair =>
                !Enum.IsDefined(pair.Key) || pair.Value is < -1000 or > 1000))
            throw new ArgumentOutOfRangeException(nameof(gameplayPoints));
        KillPoints = killPoints;
        AssistPoints = assistPoints;
        DeathPenalty = deathPenalty;
        GameplayPoints = (gameplayPoints ?? new Dictionary<GameplayStatKind, int>())
            .Where(pair => pair.Value != 0).ToFrozenDictionary();
    }

    public int KillPoints { get; }
    public int AssistPoints { get; }
    public int DeathPenalty { get; }
    public IReadOnlyDictionary<GameplayStatKind, int> GameplayPoints { get; }
}

public interface IGameplayRankScoreRepository : ICombatRepository
{
    ValueTask<IReadOnlyList<CombatScoreRankEntry>> GetTopScoresAsync(
        RankScoreWeights weights, int limit, int offset,
        CancellationToken cancellationToken = default);

    ValueTask<CombatScoreRankEntry?> GetScorePlacementAsync(PlayerId playerId,
        RankScoreWeights weights, CancellationToken cancellationToken = default);

    // Includes combat and effective gameplay events, before adjustment and flooring.
    ValueTask<long> ReadRawScoreAsync(PlayerId playerId, RankScoreWeights weights,
        CancellationToken cancellationToken = default);
}
