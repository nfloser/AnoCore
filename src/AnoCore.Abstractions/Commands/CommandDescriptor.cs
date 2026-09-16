using System.Text.RegularExpressions;
using AnoCore.Abstractions.Permissions;

namespace AnoCore.Abstractions.Commands;

public sealed record CommandDescriptor
{
    private static readonly Regex ValidCommandPattern = new(
        "^[a-z0-9][a-z0-9_-]{0,31}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public CommandDescriptor(
        string name,
        string description,
        PermissionId? permission = null,
        IEnumerable<string>? aliases = null)
    {
        Name = NormalizeCommand(name, nameof(name));
        Description = description?.Trim() ?? string.Empty;
        Permission = permission;

        Aliases = (aliases ?? [])
            .Select(alias => NormalizeCommand(alias, nameof(aliases)))
            .Where(alias => !string.Equals(alias, Name, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public string Name { get; }

    public string Description { get; }

    public PermissionId? Permission { get; }

    public IReadOnlyList<string> Aliases { get; }

    private static string NormalizeCommand(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A command name is required.", parameterName);
        }

        var normalized = value.Trim().TrimStart('!').ToLowerInvariant();
        if (!ValidCommandPattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "Command names may contain only letters, numbers, underscores and hyphens.",
                parameterName);
        }

        return normalized;
    }
}
