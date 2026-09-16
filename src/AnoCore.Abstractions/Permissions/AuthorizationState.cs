using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Permissions;

public sealed class AuthorizationState : IEquatable<AuthorizationState>
{
    public AuthorizationState(
        IReadOnlyCollection<AuthorizationRole> roles,
        IReadOnlyCollection<PlayerAuthorization> players)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(players);

        Roles = roles.OrderBy(role => role.Id.Value, StringComparer.Ordinal).ToArray();
        Players = players.OrderBy(player => player.PlayerId.SteamId64).ToArray();
        Validate();
    }

    public static AuthorizationState Empty { get; } = new([], []);

    public IReadOnlyCollection<AuthorizationRole> Roles { get; }

    public IReadOnlyCollection<PlayerAuthorization> Players { get; }

    public bool Equals(AuthorizationState? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Roles.Count != other.Roles.Count || Players.Count != other.Players.Count)
        {
            return false;
        }

        return Roles.Zip(other.Roles).All(pair => RoleEquals(pair.First, pair.Second))
            && Players.Zip(other.Players).All(pair => PlayerEquals(pair.First, pair.Second));
    }

    public override bool Equals(object? obj) => Equals(obj as AuthorizationState);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var role in Roles)
        {
            hash.Add(role.Id);
            hash.Add(role.Immunity);
        }

        foreach (var player in Players)
        {
            hash.Add(player.PlayerId);
        }

        return hash.ToHashCode();
    }

    private void Validate()
    {
        var rolesById = new Dictionary<RoleId, AuthorizationRole>();
        foreach (var role in Roles)
        {
            if (!rolesById.TryAdd(role.Id, role))
            {
                throw new ArgumentException($"Role '{role.Id}' is defined more than once.", nameof(Roles));
            }
        }

        foreach (var role in Roles)
        {
            foreach (var parent in role.Parents)
            {
                if (!rolesById.ContainsKey(parent))
                {
                    throw new ArgumentException($"Role '{role.Id}' references unknown parent '{parent}'.", nameof(Roles));
                }
            }
        }

        var visited = new HashSet<RoleId>();
        var visiting = new HashSet<RoleId>();
        foreach (var role in Roles)
        {
            Visit(role.Id, rolesById, visited, visiting);
        }

        var playerIds = new HashSet<PlayerId>();
        foreach (var player in Players)
        {
            if (!playerIds.Add(player.PlayerId))
            {
                throw new ArgumentException($"Player '{player.PlayerId}' has duplicate authorization assignments.", nameof(Players));
            }

            foreach (var role in player.Roles)
            {
                if (!rolesById.ContainsKey(role))
                {
                    throw new ArgumentException($"Player '{player.PlayerId}' references unknown role '{role}'.", nameof(Players));
                }
            }
        }
    }

    private static void Visit(
        RoleId id,
        IReadOnlyDictionary<RoleId, AuthorizationRole> roles,
        ISet<RoleId> visited,
        ISet<RoleId> visiting)
    {
        if (visited.Contains(id))
        {
            return;
        }

        if (!visiting.Add(id))
        {
            throw new ArgumentException($"Cyclic role inheritance detected at '{id}'.", "roles");
        }

        foreach (var parent in roles[id].Parents)
        {
            Visit(parent, roles, visited, visiting);
        }

        visiting.Remove(id);
        visited.Add(id);
    }

    private static bool RoleEquals(AuthorizationRole left, AuthorizationRole right)
        => left.Id == right.Id
            && left.Immunity == right.Immunity
            && left.Parents.SequenceEqual(right.Parents)
            && left.Rules.SequenceEqual(right.Rules)
            && left.Tags.SequenceEqual(right.Tags, StringComparer.Ordinal);

    private static bool PlayerEquals(PlayerAuthorization left, PlayerAuthorization right)
        => left.PlayerId == right.PlayerId
            && left.Roles.SequenceEqual(right.Roles)
            && left.Rules.SequenceEqual(right.Rules);
}
