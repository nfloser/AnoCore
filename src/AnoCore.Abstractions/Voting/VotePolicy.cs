namespace AnoCore.Abstractions.Voting;

public sealed record VotePolicy
{
    public VotePolicy(TimeSpan duration, int minimumVotes, VoteTieBreakPolicy tieBreakPolicy = VoteTieBreakPolicy.OptionOrder)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration), "Vote duration must be positive.");
        if (minimumVotes <= 0) throw new ArgumentOutOfRangeException(nameof(minimumVotes), "Minimum votes must be positive.");
        Duration = duration; MinimumVotes = minimumVotes; TieBreakPolicy = tieBreakPolicy;
    }
    public TimeSpan Duration { get; }
    public int MinimumVotes { get; }
    public VoteTieBreakPolicy TieBreakPolicy { get; }
}
