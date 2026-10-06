using System.Collections.Frozen;
using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Stats;

public sealed class RankScoreWeights
{
    public const long MaximumStartingPoints = 1_000_000_000;

    public RankScoreWeights(int killPoints, int assistPoints, int deathPenalty,
        IReadOnlyDictionary<GameplayStatKind, int>? gameplayPoints = null)
        : this(killPoints, assistPoints, deathPenalty, 0, gameplayPoints)
    {
    }

    public RankScoreWeights(int killPoints, int assistPoints, int deathPenalty,
        long startingPoints, IReadOnlyDictionary<GameplayStatKind, int>? gameplayPoints = null)
    {
        if (killPoints is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(killPoints));
        if (assistPoints is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(assistPoints));
        if (deathPenalty is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(deathPenalty));
        if (startingPoints is < 0 or > MaximumStartingPoints)
            throw new ArgumentOutOfRangeException(nameof(startingPoints));
        if (gameplayPoints is not null && gameplayPoints.Any(pair =>
                !Enum.IsDefined(pair.Key) || pair.Value is < -1000 or > 1000))
            throw new ArgumentOutOfRangeException(nameof(gameplayPoints));
        KillPoints = killPoints;
        AssistPoints = assistPoints;
        DeathPenalty = deathPenalty;
        StartingPoints = startingPoints;
        GameplayPoints = (gameplayPoints ?? new Dictionary<GameplayStatKind, int>())
            .Where(pair => pair.Value != 0).ToFrozenDictionary();
    }

    public int KillPoints { get; }
    public int AssistPoints { get; }
    public int DeathPenalty { get; }
    public long StartingPoints { get; }
    public IReadOnlyDictionary<GameplayStatKind, int> GameplayPoints { get; }
}

public interface IGameplayRankScoreRepository : ICombatRepository
{
    ValueTask<IReadOnlyList<CombatScoreRankEntry>> GetTopScoresAsync(
        RankScoreWeights weights, int limit, int offset,
        CancellationToken cancellationToken = default);

    ValueTask<CombatScoreRankEntry?> GetScorePlacementAsync(PlayerId playerId,
        RankScoreWeights weights, CancellationToken cancellationToken = default);

    // Includes the configured starting baseline plus combat and effective gameplay events, before adjustment and flooring.
    ValueTask<long> ReadRawScoreAsync(PlayerId playerId, RankScoreWeights weights,
        CancellationToken cancellationToken = default);
}
