using System.Text.RegularExpressions;

namespace AnoCore.Abstractions.Permissions;

public sealed record RoleId
{
    private static readonly Regex ValidPattern = new(
        "^[a-z0-9][a-z0-9._-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public RoleId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A role id is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!ValidPattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "Role ids may contain only lowercase letters, numbers, dots, underscores and hyphens.",
                nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
