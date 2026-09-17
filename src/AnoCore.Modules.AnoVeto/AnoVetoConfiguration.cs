using AnoCore.Abstractions.Voting;

namespace AnoCore.Modules.AnoVeto;

public sealed class AnoVetoConfiguration
{
    public bool Enabled { get; set; } = true;

    public int DurationSeconds { get; set; } = 30;

    public int MinimumVotes { get; set; } = 1;

    public VoteTieBreakPolicy TieBreakPolicy { get; set; } = VoteTieBreakPolicy.OptionOrder;

    public AnoVetoOptions ToOptions()
        => new(TimeSpan.FromSeconds(DurationSeconds), MinimumVotes, TieBreakPolicy);

    public static IReadOnlyCollection<string> Validate(AnoVetoConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = new List<string>();

        if (configuration.DurationSeconds <= 0)
        {
            errors.Add("DurationSeconds must be greater than zero.");
        }

        if (configuration.MinimumVotes <= 0)
        {
            errors.Add("MinimumVotes must be greater than zero.");
        }

        if (!Enum.IsDefined(typeof(VoteTieBreakPolicy), configuration.TieBreakPolicy))
        {
            errors.Add("TieBreakPolicy is invalid.");
        }

        return errors;
    }
}
