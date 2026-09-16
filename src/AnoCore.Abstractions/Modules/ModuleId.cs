using System.Text.RegularExpressions;

namespace AnoCore.Abstractions.Modules;

public readonly record struct ModuleId
{
    private static readonly Regex ValidPattern = new(
        "^[a-z0-9][a-z0-9.-]{0,62}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ModuleId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A module id is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!ValidPattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "Module ids may contain only lowercase letters, numbers, dots and hyphens.",
                nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
