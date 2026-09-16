using System.Text.RegularExpressions;

namespace AnoCore.Abstractions.Permissions;

public readonly record struct PermissionId
{
    private static readonly Regex ValidPattern = new(
        "^ano(?:\\.[a-z0-9][a-z0-9_-]*)+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public PermissionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A permission id is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!ValidPattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "Permissions must use the ano.* namespace and contain only letters, numbers, underscores and dots.",
                nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
