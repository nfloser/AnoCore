using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Voting;

public sealed record VoteSnapshot(
    VoteDefinition Definition,
    VoteState State,
    DateTimeOffset OpenedAt,
    DateTimeOffset Deadline,
    IReadOnlyDictionary<PlayerId, string> Ballots);
