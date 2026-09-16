using System.Text.RegularExpressions;

namespace AnoCore.Abstractions.Voting;

public sealed record VoteOption
{
    private static readonly Regex IdPattern = new(
        "^[a-z0-9][a-z0-9_.-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public VoteOption(string id, string label)
    {
        if (string.IsNullOrWhiteSpace(id) || !IdPattern.IsMatch(id.Trim().ToLowerInvariant()))
        {
            throw new ArgumentException("A lowercase-safe vote option ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("A vote option label is required.", nameof(label));
        }

        Id = id.Trim().ToLowerInvariant();
        Label = label.Trim();
    }

    public string Id { get; }

    public string Label { get; }
}
