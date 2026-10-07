using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Permissions;

public sealed class PlayerAuthorization
{
    public PlayerAuthorization(PlayerId playerId, IReadOnlyCollection<RoleId> roles, IReadOnlyCollection<PermissionRule> rules)
        : this(playerId, roles, rules, null) { }

    [System.Text.Json.Serialization.JsonConstructor]
    public PlayerAuthorization(
        PlayerId playerId,
        IReadOnlyCollection<RoleId> roles,
        IReadOnlyCollection<PermissionRule> rules,
        IReadOnlyCollection<TimedRoleAssignment>? timedRoles)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(rules);

        PlayerId = playerId;
        Roles = roles.Distinct().OrderBy(role => role.Value, StringComparer.Ordinal).ToArray();
        Rules = rules.ToArray();
        TimedRoles = (timedRoles ?? []).OrderBy(grant => grant.Role.Value, StringComparer.Ordinal).ToArray();
        if (TimedRoles.Select(grant => grant.Role).Distinct().Count() != TimedRoles.Count
            || TimedRoles.Any(grant => Roles.Contains(grant.Role)))
            throw new ArgumentException("A role can have only one permanent or timed assignment.", nameof(timedRoles));
    }

    public IReadOnlyCollection<TimedRoleAssignment> TimedRoles { get; }

    public PlayerId PlayerId { get; }

    public IReadOnlyCollection<RoleId> Roles { get; }

    public IReadOnlyCollection<PermissionRule> Rules { get; }
}
