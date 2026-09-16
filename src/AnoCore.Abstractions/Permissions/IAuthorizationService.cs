using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Permissions;

public interface IAuthorizationService : IPermissionEvaluator
{
    ValueTask<AuthorizationDecision> EvaluateAsync(
        PlayerId playerId,
        PermissionId permission,
        CancellationToken cancellationToken = default);

    ValueTask<int> GetImmunityAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> CanTargetAsync(
        PlayerId actor,
        PlayerId target,
        CancellationToken cancellationToken = default);

    ValueTask<bool> HasTagAsync(
        PlayerId playerId,
        string tag,
        CancellationToken cancellationToken = default);

    ValueTask ReloadAsync(CancellationToken cancellationToken = default);
}
