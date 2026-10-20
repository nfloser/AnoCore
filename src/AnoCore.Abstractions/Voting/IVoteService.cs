using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Voting;

public interface IVoteService
{
    ValueTask<VoteOperationResult> CreateAsync(
        PlayerId caller,
        VoteDefinition definition,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    ValueTask<VoteOperationResult> CastAsync(
        VoteId voteId,
        PlayerId playerId,
        string optionId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    // Atomically submit a complete ballot. Implementations without this capability fail closed.
    ValueTask<VoteOperationResult> SubmitBallotAsync(
        VoteId voteId,
        PlayerId playerId,
        IReadOnlyCollection<string> optionIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(VoteOperationResult.Reject(VoteOperationFailure.InvalidOption));

    ValueTask<VoteOperationResult> CloseAsync(
        PlayerId caller,
        VoteId voteId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    ValueTask<VoteOperationResult> CancelAsync(
        PlayerId caller,
        VoteId voteId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    VoteResult? FinalizeIfAllEligibleVoted(VoteId voteId, DateTimeOffset now);

    IReadOnlyList<VoteResult> FinalizeExpired(DateTimeOffset now);

    bool TryGet(VoteId voteId, out VoteSnapshot? vote);
}
