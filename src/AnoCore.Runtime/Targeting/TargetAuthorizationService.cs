using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Targeting;

namespace AnoCore.Runtime.Targeting;

public sealed class TargetAuthorizationService : ITargetAuthorizationService
{
    private readonly IPlayerRegistry _players;
    private readonly IAuthorizationService _authorization;

    public TargetAuthorizationService(
        IPlayerRegistry players,
        IAuthorizationService authorization)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    public async ValueTask<TargetAuthorizationDecision> AuthorizeAsync(
        PlayerId actor,
        PlayerSnapshot target,
        PermissionId permission,
        bool allowSelf = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(permission);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_players.TryGet(actor, out var currentActor)
            || currentActor is null
            || !currentActor.IsConnected)
        {
            return TargetAuthorizationDecision.Denied(TargetAuthorizationFailure.ActorNotOnline);
        }

        if (!await _authorization.HasPermissionAsync(actor, permission, cancellationToken).ConfigureAwait(false))
        {
            return TargetAuthorizationDecision.Denied(TargetAuthorizationFailure.PermissionDenied);
        }

        if (actor == target.Id && !allowSelf)
        {
            return TargetAuthorizationDecision.Denied(TargetAuthorizationFailure.SelfTargetNotAllowed);
        }

        if (!_players.TryGet(target.Id, out var currentTarget)
            || currentTarget is null
            || !currentTarget.IsConnected)
        {
            return TargetAuthorizationDecision.Denied(TargetAuthorizationFailure.TargetNotOnline);
        }

        if (currentTarget.SessionId != target.SessionId)
        {
            return TargetAuthorizationDecision.Denied(TargetAuthorizationFailure.StaleTarget);
        }

        if (actor == target.Id)
        {
            return TargetAuthorizationDecision.Allowed;
        }

        if (!await _authorization.CanTargetAsync(actor, target.Id, cancellationToken).ConfigureAwait(false))
        {
            return TargetAuthorizationDecision.Denied(TargetAuthorizationFailure.TargetImmune);
        }

        return TargetAuthorizationDecision.Allowed;
    }
}
