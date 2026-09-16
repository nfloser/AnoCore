using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Voting;

public sealed class VoteDefinition
{
    public VoteDefinition(
        VoteId id,
        string title,
        IReadOnlyCollection<VoteOption> options,
        IReadOnlyCollection<PlayerId> eligiblePlayers,
        VotePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(eligiblePlayers);
        ArgumentNullException.ThrowIfNull(policy);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A vote title is required.", nameof(title));
        }

        var optionArray = options.ToArray();
        if (optionArray.Length < 2)
        {
            throw new ArgumentException("A vote requires at least two options.", nameof(options));
        }

        if (optionArray.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count() != optionArray.Length)
        {
            throw new ArgumentException("Vote option IDs must be unique.", nameof(options));
        }

        var eligibleArray = eligiblePlayers.Distinct().ToArray();
        if (eligibleArray.Length == 0)
        {
            throw new ArgumentException("A vote requires at least one eligible player.", nameof(eligiblePlayers));
        }

        if (policy.MinimumVotes > eligibleArray.Length)
        {
            throw new ArgumentException("Minimum votes cannot exceed the eligible population.", nameof(policy));
        }

        Id = id;
        Title = title.Trim();
        Options = optionArray;
        EligiblePlayers = eligibleArray;
        Policy = policy;
    }

    public VoteId Id { get; }

    public string Title { get; }

    public IReadOnlyList<VoteOption> Options { get; }

    public IReadOnlyCollection<PlayerId> EligiblePlayers { get; }

    public VotePolicy Policy { get; }
}
