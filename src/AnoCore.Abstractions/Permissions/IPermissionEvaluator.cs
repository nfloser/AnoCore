using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Permissions;

public interface IPermissionEvaluator
{
    ValueTask<bool> HasPermissionAsync(
        PlayerId playerId,
        PermissionId permission,
        CancellationToken cancellationToken = default);
}
