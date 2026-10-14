namespace AnoCore.Abstractions.Voting;

public sealed record VotePolicy
{
    public VotePolicy(TimeSpan duration, int minimumVotes, VoteTieBreakPolicy tieBreakPolicy = VoteTieBreakPolicy.OptionOrder, int maxVotesPerPlayer = 1)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration), "Vote duration must be positive.");
        if (minimumVotes <= 0) throw new ArgumentOutOfRangeException(nameof(minimumVotes), "Minimum votes must be positive.");
        if (maxVotesPerPlayer is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(maxVotesPerPlayer), "Votes per player must be between 1 and 32.");
        Duration = duration; MinimumVotes = minimumVotes; TieBreakPolicy = tieBreakPolicy;
        MaxVotesPerPlayer = maxVotesPerPlayer;
    }
    public TimeSpan Duration { get; }
    public int MinimumVotes { get; }
    public VoteTieBreakPolicy TieBreakPolicy { get; }
    public int MaxVotesPerPlayer { get; }
}
