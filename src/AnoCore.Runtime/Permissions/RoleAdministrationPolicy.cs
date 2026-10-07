using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;

namespace AnoCore.Runtime.Permissions;

public static class RoleAdministrationPolicy
{
    public static async ValueTask<AuthorizationState> ChangeAsync(AuthorizationState state, PlayerId? actor,
        PlayerId target, RoleId role, DateTimeOffset? expiresAtUtc, bool revoke, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(role);
        if (!state.Roles.Any(value => value.Id == role)) throw new ArgumentException("Unknown role.", nameof(role));
        if (!revoke && expiresAtUtc is { } expiry && (expiry <= now || expiry > now.AddDays(3650)))
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Timed grants must expire within ten years.");
        if (actor is not null)
        {
            var service = new AuthorizationService(new SnapshotStore(state), state, new FixedClock(now));
            if (actor == target || !await service.HasPermissionAsync(actor, new("ano.admin.roles." + (revoke ? "revoke" : "grant")))
                || !await service.CanTargetAsync(actor, target)) throw new UnauthorizedAccessException("Role administration denied.");
            // Delegation is restricted to permanently held roles, including their parents.
            var held = new HashSet<RoleId>();
            void Expand(RoleId id)
            {
                if (!held.Add(id)) return;
                foreach (var parent in state.Roles.Single(value => value.Id == id).Parents) Expand(parent);
            }
            foreach (var id in state.Players.Single(value => value.PlayerId == actor).Roles) Expand(id);
            var actorPolicy = state.Players.Single(value => value.PlayerId == actor);
            if (actorPolicy.Rules.Any(rule => rule.Effect == PermissionEffect.Deny)
                || held.SelectMany(id => state.Roles.Single(value => value.Id == id).Rules).Any(rule => rule.Effect == PermissionEffect.Deny))
                throw new UnauthorizedAccessException("A restricted actor cannot delegate role policy.");
            var roleImmunity = EffectiveImmunity(role, state);
            if (!held.Contains(role) || roleImmunity >= await service.GetImmunityAsync(actor))
                throw new UnauthorizedAccessException("Cannot delegate a role outside the permanent authority ceiling.");
        }
        var previous = state.Players.FirstOrDefault(value => value.PlayerId == target) ?? new PlayerAuthorization(target, [], []);
        var permanent = previous.Roles.Where(value => value != role).ToList();
        var timed = previous.TimedRoles.Where(value => value.Role != role).ToList();
        if (!revoke)
        {
            if (expiresAtUtc is { } end) timed.Add(new(role, end));
            else permanent.Add(role);
        }
        var replacement = new PlayerAuthorization(target, permanent, previous.Rules, timed);
        return new(state.Roles, state.Players.Where(value => value.PlayerId != target).Append(replacement).ToArray());
    }

    private static int EffectiveImmunity(RoleId id, AuthorizationState state)
    {
        var role = state.Roles.Single(value => value.Id == id);
        return role.Parents.Select(parent => EffectiveImmunity(parent, state)).Append(role.Immunity).Max();
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class SnapshotStore(AuthorizationState state) : IAuthorizationStore
    {
        public ValueTask<AuthorizationState?> LoadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<AuthorizationState?>(state);
        public ValueTask SaveAsync(AuthorizationState value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
