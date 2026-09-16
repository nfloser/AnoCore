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
        IEnumerable<string>? aliases = null,
        IEnumerable<CommandArgumentDescriptor>? arguments = null,
        string? usage = null)
    {
        Name = NormalizeCommand(name, nameof(name));
        Description = description?.Trim() ?? string.Empty;
        Permission = permission;

        Aliases = (aliases ?? [])
            .Select(alias => NormalizeCommand(alias, nameof(aliases)))
            .Where(alias => !string.Equals(alias, Name, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var argumentArray = (arguments ?? []).ToArray();
        if (argumentArray.Select(argument => argument.Name).Distinct(StringComparer.Ordinal).Count() != argumentArray.Length)
        {
            throw new ArgumentException("Command argument names must be unique.", nameof(arguments));
        }

        var optionalSeen = false;
        foreach (var argument in argumentArray)
        {
            if (!argument.Required)
            {
                optionalSeen = true;
            }
            else if (optionalSeen)
            {
                throw new ArgumentException("Required command arguments cannot follow optional arguments.", nameof(arguments));
            }
        }

        Arguments = argumentArray;
        Usage = string.IsNullOrWhiteSpace(usage) ? BuildUsage(Name, argumentArray) : usage.Trim();
    }

    public string Name { get; }

    public string Description { get; }

    public PermissionId? Permission { get; }

    public IReadOnlyList<string> Aliases { get; }

    public IReadOnlyList<CommandArgumentDescriptor> Arguments { get; }

    public string Usage { get; }

    private static string BuildUsage(string name, IReadOnlyCollection<CommandArgumentDescriptor> arguments)
        => arguments.Count == 0
            ? name
            : $"{name} {string.Join(' ', arguments.Select(argument => argument.Required ? $"<{argument.Name}>" : $"[{argument.Name}]"))}";

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
