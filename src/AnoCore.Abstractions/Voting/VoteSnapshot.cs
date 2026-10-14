using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Voting;

// Ballots retains the first selected option per player for single-choice consumers.
// Selections is the authoritative ordered ballot set for multi-choice votes.
public sealed record VoteSnapshot(
    VoteDefinition Definition,
    VoteState State,
    DateTimeOffset OpenedAt,
    DateTimeOffset Deadline,
    IReadOnlyDictionary<PlayerId, string> Ballots)
{
    public IReadOnlyDictionary<PlayerId, IReadOnlyList<string>> Selections { get; init; }
        = new Dictionary<PlayerId, IReadOnlyList<string>>();
}
