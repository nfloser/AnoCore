using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

internal static class RankScoreQueries
{
    public static void ValidateRepository(ICombatRepository repository, RankConfiguration configuration)
    {
        if ((configuration.Source == RankScoreSource.EventLedger
                || configuration.StartingPoints != 0
                || configuration.GameplayPoints.Any(pair => pair.Value != 0))
            && repository is not IGameplayRankScoreRepository)
            throw new ArgumentException(
                "Configured starting/gameplay rank points require a combined rank score repository.",
                nameof(repository));
    }

    public static ValueTask<CombatScoreRankEntry?> PlacementAsync(
        ICombatRepository repository, RankConfiguration configuration, PlayerId player,
        CancellationToken cancellationToken)
    {
        ValidateRepository(repository, configuration);
        return repository is IGameplayRankScoreRepository combined
            ? combined.GetScorePlacementAsync(player, configuration.ScoreWeights, cancellationToken)
            : repository.GetScorePlacementAsync(player, configuration.KillPoints,
                configuration.AssistPoints, configuration.DeathPenalty, cancellationToken);
    }

    public static ValueTask<IReadOnlyList<CombatScoreRankEntry>> TopAsync(
        ICombatRepository repository, RankConfiguration configuration, int limit, int offset,
        CancellationToken cancellationToken)
    {
        ValidateRepository(repository, configuration);
        return repository is IGameplayRankScoreRepository combined
            ? combined.GetTopScoresAsync(configuration.ScoreWeights, limit, offset, cancellationToken)
            : repository.GetTopScoresAsync(configuration.KillPoints, configuration.AssistPoints,
                configuration.DeathPenalty, limit, offset, cancellationToken);
    }
}
