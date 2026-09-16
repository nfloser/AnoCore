using AnoCore.Abstractions.Players;
namespace AnoCore.Abstractions.Voting;

public interface IVoteService
{
    ValueTask<VoteOperationResult> CreateAsync(PlayerId caller, VoteDefinition definition, DateTimeOffset now, CancellationToken cancellationToken = default);
    ValueTask<VoteOperationResult> CastAsync(VoteId voteId, PlayerId playerId, string optionId, DateTimeOffset now, CancellationToken cancellationToken = default);
    ValueTask<VoteOperationResult> CloseAsync(PlayerId caller, VoteId voteId, DateTimeOffset now, CancellationToken cancellationToken = default);
    ValueTask<VoteOperationResult> CancelAsync(PlayerId caller, VoteId voteId, DateTimeOffset now, CancellationToken cancellationToken = default);
    IReadOnlyList<VoteResult> FinalizeExpired(DateTimeOffset now);
    bool TryGet(VoteId voteId, out VoteSnapshot? vote);
}
