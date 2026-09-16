using System.Text.RegularExpressions;

namespace AnoCore.Abstractions.Commands;

public sealed record CommandArgumentDescriptor
{
    private static readonly Regex NamePattern = new(
        "^[a-z][a-z0-9_-]{0,31}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public CommandArgumentDescriptor(
        string name,
        CommandArgumentKind kind,
        string description,
        bool required = true)
    {
        if (string.IsNullOrWhiteSpace(name) || !NamePattern.IsMatch(name.Trim()))
        {
            throw new ArgumentException("Command argument names must use lowercase-safe identifiers.", nameof(name));
        }

        Name = name.Trim();
        Kind = kind;
        Description = description?.Trim() ?? string.Empty;
        Required = required;
    }

    public string Name { get; }

    public CommandArgumentKind Kind { get; }

    public string Description { get; }

    public bool Required { get; }
}
