using System.Text.RegularExpressions;

namespace AnoCore.Abstractions.Voting;

public sealed record VoteId
{
    private static readonly Regex Pattern = new(
        "^ano\\.[a-z0-9][a-z0-9_.-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public VoteId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A vote ID is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!Pattern.IsMatch(normalized))
        {
            throw new ArgumentException("Vote IDs must use the 'ano.' namespace.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
