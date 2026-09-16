using System.Text.RegularExpressions;

namespace AnoCore.Abstractions.Permissions;

public sealed class AuthorizationRole
{
    private static readonly Regex ValidTag = new(
        "^[a-z0-9][a-z0-9._-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public AuthorizationRole(
        RoleId id,
        int immunity,
        IReadOnlyCollection<RoleId> parents,
        IReadOnlyCollection<PermissionRule> rules,
        IReadOnlyCollection<string> tags)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(parents);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(tags);
        if (immunity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(immunity), "Immunity cannot be negative.");
        }

        var normalizedTags = tags.Select(NormalizeTag).Distinct(StringComparer.Ordinal).Order().ToArray();
        Id = id;
        Immunity = immunity;
        Parents = parents.Distinct().OrderBy(parent => parent.Value, StringComparer.Ordinal).ToArray();
        Rules = rules.ToArray();
        Tags = normalizedTags;
    }

    public RoleId Id { get; }

    public int Immunity { get; }

    public IReadOnlyCollection<RoleId> Parents { get; }

    public IReadOnlyCollection<PermissionRule> Rules { get; }

    public IReadOnlyCollection<string> Tags { get; }

    private static string NormalizeTag(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Role tags cannot be empty.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!ValidTag.IsMatch(normalized))
        {
            throw new ArgumentException("Role tags contain invalid characters.", nameof(value));
        }

        return normalized;
    }
}
