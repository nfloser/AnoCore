using System.Text.RegularExpressions;

namespace AnoCore.Abstractions.Menus;

public readonly record struct MenuId
{
    private static readonly Regex Pattern = new(
        "^ano\\.[a-z0-9][a-z0-9_.-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public MenuId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A menu ID is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!Pattern.IsMatch(normalized))
        {
            throw new ArgumentException("Menu IDs must use the 'ano.' namespace and contain only lowercase-safe characters.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
