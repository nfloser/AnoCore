namespace AnoCore.Abstractions.Voting;

public sealed record VoteOperationResult(bool Accepted, VoteOperationFailure Failure, VoteResult? Result = null)
{
    public static VoteOperationResult Success(VoteResult? result = null) => new(true, VoteOperationFailure.None, result);

    public static VoteOperationResult Reject(VoteOperationFailure failure) => new(false, failure);
}
