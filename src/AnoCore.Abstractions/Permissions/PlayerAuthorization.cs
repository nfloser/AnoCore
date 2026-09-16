using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Permissions;

public sealed class PlayerAuthorization
{
    public PlayerAuthorization(
        PlayerId playerId,
        IReadOnlyCollection<RoleId> roles,
        IReadOnlyCollection<PermissionRule> rules)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(rules);

        PlayerId = playerId;
        Roles = roles.Distinct().OrderBy(role => role.Value, StringComparer.Ordinal).ToArray();
        Rules = rules.ToArray();
    }

    public PlayerId PlayerId { get; }

    public IReadOnlyCollection<RoleId> Roles { get; }

    public IReadOnlyCollection<PermissionRule> Rules { get; }
}
