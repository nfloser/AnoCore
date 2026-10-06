using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression;

public sealed record ChallengeCompletionRecord(
    string ChallengeId,
    int DefinitionVersion,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    ProgressionGrantRecord Grant);

public sealed record ChallengeCompletionResult(
    bool Applied,
    ChallengeEvaluation Evaluation,
    ChallengeCompletionRecord? Completion);

public interface IChallengeRepository
{
    ValueTask<ChallengeEvaluation> ReadAsync(PlayerId playerId, ChallengeCatalogSnapshot catalog,
        string challengeId, DateTimeOffset at, CancellationToken cancellationToken = default);

    ValueTask<ChallengeCompletionResult> CompleteAsync(PlayerId playerId, ChallengeCatalogSnapshot catalog,
        string challengeId, DateTimeOffset at, ProgressionDefinitionSnapshot xpDefinitions,
        CancellationToken cancellationToken = default);
}
