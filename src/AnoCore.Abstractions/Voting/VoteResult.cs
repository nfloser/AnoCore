namespace AnoCore.Abstractions.Voting;

public sealed record VoteResult(VoteId VoteId, VoteOutcome Outcome, string? WinningOptionId, IReadOnlyDictionary<string, int> Tallies, int VotesCast, DateTimeOffset FinalizedAt);
