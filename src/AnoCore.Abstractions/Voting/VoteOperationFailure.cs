namespace AnoCore.Abstractions.Voting;

public enum VoteOperationFailure
{
    None = 0,
    Forbidden = 1,
    AlreadyExists = 2,
    NotFound = 3,
    NotOpen = 4,
    NotEligible = 5,
    AlreadyVoted = 6,
    InvalidOption = 7,
}
