using AnoCore.Abstractions.Voting;

namespace AnoCore.Modules.AnoVeto;

public sealed record AnoVetoOptions
{
    public AnoVetoOptions(
        TimeSpan duration,
        int minimumVotes,
        VoteTieBreakPolicy tieBreakPolicy = VoteTieBreakPolicy.OptionOrder,
        int maxVotesPerPlayer = 3)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        if (minimumVotes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumVotes));
        }

        if (maxVotesPerPlayer is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(maxVotesPerPlayer));
        }

        Duration = duration;
        MinimumVotes = minimumVotes;
        TieBreakPolicy = tieBreakPolicy;
        MaxVotesPerPlayer = maxVotesPerPlayer;
    }

    public TimeSpan Duration { get; }
    public int MinimumVotes { get; }
    public VoteTieBreakPolicy TieBreakPolicy { get; }
    public int MaxVotesPerPlayer { get; }
}
