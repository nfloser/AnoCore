namespace AnoCore.Abstractions.Permissions;

public sealed record PermissionRule
{
    public PermissionRule(string pattern, PermissionEffect effect)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("A permission pattern is required.", nameof(pattern));
        }

        var normalized = pattern.Trim().ToLowerInvariant();
        var wildcard = normalized.EndsWith(".*", StringComparison.Ordinal);
        if (wildcard)
        {
            var prefix = normalized[..^2];
            if (!string.Equals(prefix, "ano", StringComparison.Ordinal))
            {
                _ = new PermissionId(prefix);
            }
        }
        else
        {
            _ = new PermissionId(normalized);
        }

        if (normalized.Contains('*') && !wildcard)
        {
            throw new ArgumentException("Wildcards are allowed only as the final .* segment.", nameof(pattern));
        }

        Pattern = normalized;
        Effect = effect;
        IsWildcard = wildcard;
        Specificity = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Count(segment => segment != "*") * 2 + (wildcard ? 0 : 1);
    }

    public string Pattern { get; }

    public PermissionEffect Effect { get; }

    public bool IsWildcard { get; }

    public int Specificity { get; }

    public bool Matches(PermissionId permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        if (!IsWildcard)
        {
            return string.Equals(Pattern, permission.Value, StringComparison.Ordinal);
        }

        var prefix = Pattern[..^2];
        return permission.Value.StartsWith($"{prefix}.", StringComparison.Ordinal);
    }
}
