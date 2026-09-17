using AnoCore.Abstractions.Voting;

namespace AnoCore.Modules.AnoVeto;

public sealed record AnoVetoOptions
{
    public AnoVetoOptions(
        TimeSpan duration,
        int minimumVotes,
        VoteTieBreakPolicy tieBreakPolicy = VoteTieBreakPolicy.OptionOrder)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        if (minimumVotes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumVotes));
        }

        Duration = duration;
        MinimumVotes = minimumVotes;
        TieBreakPolicy = tieBreakPolicy;
    }

    public TimeSpan Duration { get; }
    public int MinimumVotes { get; }
    public VoteTieBreakPolicy TieBreakPolicy { get; }
}
