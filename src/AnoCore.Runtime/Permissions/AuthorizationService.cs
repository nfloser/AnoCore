using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;

namespace AnoCore.Runtime.Permissions;

public sealed class AuthorizationService : IAuthorizationService, IAuthorizationReloadEvents
{
    public event Action? Reloaded;

    private readonly IAuthorizationStore _store;
    private CompiledAuthorization _compiled;
    private readonly object _expiryGate = new();
    private readonly TimeProvider _clock;
    private AuthorizationState _state;
    private DateTimeOffset _nextExpiry;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    public AuthorizationService(
        IAuthorizationStore store,
        AuthorizationState? initialState = null,
        TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
        _state = initialState ?? AuthorizationState.Empty;
        _compiled = CompileActive(_state, _clock.GetUtcNow());
    }

    public async ValueTask<bool> HasPermissionAsync(
        PlayerId playerId,
        PermissionId permission,
        CancellationToken cancellationToken = default)
        => (await EvaluateAsync(playerId, permission, cancellationToken).ConfigureAwait(false)).IsAllowed;

    public ValueTask<AuthorizationDecision> EvaluateAsync(
        PlayerId playerId,
        PermissionId permission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(permission);
        cancellationToken.ThrowIfCancellationRequested();

        var compiled = CurrentSnapshot();
        if (!compiled.Players.TryGetValue(playerId, out var player))
        {
            return ValueTask.FromResult(AuthorizationDecision.DeniedByDefault);
        }

        var direct = SelectRule(player.DirectRules, permission);
        if (direct is not null)
        {
            return ValueTask.FromResult(new AuthorizationDecision(
                direct.Effect == PermissionEffect.Allow,
                direct.Effect,
                AuthorizationDecisionSource.Direct,
                direct,
                null));
        }

        var roleCandidate = player.Roles
            .SelectMany(role => role.Rules
                .Where(rule => rule.Matches(permission))
                .Select(rule => new RoleRuleCandidate(role.Id, rule)))
            .OrderByDescending(candidate => candidate.Rule.Specificity)
            .ThenByDescending(candidate => candidate.Rule.Effect)
            .ThenBy(candidate => candidate.Role.Value, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Rule.Pattern, StringComparer.Ordinal)
            .FirstOrDefault();

        if (roleCandidate is null)
        {
            return ValueTask.FromResult(AuthorizationDecision.DeniedByDefault);
        }

        return ValueTask.FromResult(new AuthorizationDecision(
            roleCandidate.Rule.Effect == PermissionEffect.Allow,
            roleCandidate.Rule.Effect,
            AuthorizationDecisionSource.Role,
            roleCandidate.Rule,
            roleCandidate.Role));
    }

    public ValueTask<int> GetImmunityAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        cancellationToken.ThrowIfCancellationRequested();
        var compiled = CurrentSnapshot();
        return ValueTask.FromResult(
            compiled.Players.TryGetValue(playerId, out var player) ? player.Immunity : 0);
    }

    public async ValueTask<bool> CanTargetAsync(
        PlayerId actor,
        PlayerId target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        if (actor == target)
        {
            return true;
        }

        var actorImmunity = await GetImmunityAsync(actor, cancellationToken).ConfigureAwait(false);
        var targetImmunity = await GetImmunityAsync(target, cancellationToken).ConfigureAwait(false);
        return actorImmunity > targetImmunity;
    }

    public ValueTask<bool> HasTagAsync(
        PlayerId playerId,
        string tag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new ArgumentException("A role tag is required.", nameof(tag));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var normalized = tag.Trim().ToLowerInvariant();
        var compiled = CurrentSnapshot();
        return ValueTask.FromResult(
            compiled.Players.TryGetValue(playerId, out var player)
            && player.Tags.Contains(normalized));
    }

    public async ValueTask ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false) ?? AuthorizationState.Empty;
            lock (_expiryGate)
            {
                var replacement = CompileActive(state, _clock.GetUtcNow());
                _state = state;
                Volatile.Write(ref _compiled, replacement);
            }
        }
        finally { _reloadGate.Release(); }
        NotifyReloaded();
    }

    public void FailClosed()
    {
        lock (_expiryGate)
        {
            _state = AuthorizationState.Empty;
            Volatile.Write(ref _compiled, CompileActive(_state, _clock.GetUtcNow()));
        }
        NotifyReloaded();
    }

    public void RefreshExpiredAssignments() => CurrentSnapshot();

    private CompiledAuthorization CurrentSnapshot()
    {
        var changed = false;
        CompiledAuthorization result;
        lock (_expiryGate)
        {
            var now = _clock.GetUtcNow();
            if (now >= _nextExpiry)
            {
                Volatile.Write(ref _compiled, CompileActive(_state, now));
                changed = true;
            }
            result = _compiled;
        }
        if (changed) NotifyReloaded();
        return result;
    }

    private CompiledAuthorization CompileActive(AuthorizationState state, DateTimeOffset now)
    {
        _nextExpiry = state.Players.SelectMany(player => player.TimedRoles)
            .Where(grant => grant.ExpiresAtUtc > now).Select(grant => grant.ExpiresAtUtc)
            .DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
        return CompiledAuthorization.Create(new AuthorizationState(state.Roles,
            state.Players.Select(player => new PlayerAuthorization(player.PlayerId,
                player.Roles.Concat(player.TimedRoles.Where(grant => grant.ExpiresAtUtc > now)
                    .Select(grant => grant.Role)).ToArray(), player.Rules)).ToArray()));
    }

    private void NotifyReloaded()
    {
        foreach (var handler in Reloaded?.GetInvocationList() ?? [])
        {
            try { ((Action)handler)(); }
            catch { /* Committed authorization must remain effective if an observer fails. */ }
        }
    }

    private static PermissionRule? SelectRule(
        IReadOnlyList<PermissionRule> rules,
        PermissionId permission)
        => rules
            .Where(rule => rule.Matches(permission))
            .OrderByDescending(rule => rule.Specificity)
            .ThenByDescending(rule => rule.Effect)
            .ThenBy(rule => rule.Pattern, StringComparer.Ordinal)
            .FirstOrDefault();

    private sealed record RoleRuleCandidate(RoleId Role, PermissionRule Rule);

    private sealed class CompiledAuthorization
    {
        private CompiledAuthorization(IReadOnlyDictionary<PlayerId, CompiledPlayer> players)
        {
            Players = players;
        }

        public IReadOnlyDictionary<PlayerId, CompiledPlayer> Players { get; }

        public static CompiledAuthorization Create(AuthorizationState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            var roles = state.Roles.ToDictionary(role => role.Id);
            var players = new Dictionary<PlayerId, CompiledPlayer>();

            foreach (var assignment in state.Players)
            {
                var roleIds = new HashSet<RoleId>();
                foreach (var assignedRole in assignment.Roles)
                {
                    ExpandRole(assignedRole, roles, roleIds);
                }

                var expandedRoles = roleIds
                    .Select(roleId => roles[roleId])
                    .OrderBy(role => role.Id.Value, StringComparer.Ordinal)
                    .ToArray();
                var immunity = expandedRoles.Length == 0 ? 0 : expandedRoles.Max(role => role.Immunity);
                var tags = expandedRoles
                    .SelectMany(role => role.Tags)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                players.Add(
                    assignment.PlayerId,
                    new CompiledPlayer(assignment.Rules.ToArray(), expandedRoles, immunity, tags));
            }

            return new CompiledAuthorization(players);
        }

        private static void ExpandRole(
            RoleId roleId,
            IReadOnlyDictionary<RoleId, AuthorizationRole> roles,
            ISet<RoleId> expanded)
        {
            if (!expanded.Add(roleId))
            {
                return;
            }

            foreach (var parent in roles[roleId].Parents)
            {
                ExpandRole(parent, roles, expanded);
            }
        }
    }

    private sealed record CompiledPlayer(
        IReadOnlyList<PermissionRule> DirectRules,
        IReadOnlyList<AuthorizationRole> Roles,
        int Immunity,
        IReadOnlySet<string> Tags);
}
