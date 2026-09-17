using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Targeting;

public enum TargetAuthorizationFailure
{
    None = 0,
    ActorNotOnline = 1,
    PermissionDenied = 2,
    SelfTargetNotAllowed = 3,
    TargetNotOnline = 4,
    StaleTarget = 5,
    TargetImmune = 6,
}

public sealed record TargetAuthorizationDecision(bool IsAllowed, TargetAuthorizationFailure Failure)
{
    public static readonly TargetAuthorizationDecision Allowed = new(true, TargetAuthorizationFailure.None);

    public static TargetAuthorizationDecision Denied(TargetAuthorizationFailure failure)
    {
        if (failure == TargetAuthorizationFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A denied target action requires a failure reason.");
        }

        return new TargetAuthorizationDecision(false, failure);
    }
}

public interface ITargetAuthorizationService
{
    ValueTask<TargetAuthorizationDecision> AuthorizeAsync(
        PlayerId actor,
        PlayerSnapshot target,
        PermissionId permission,
        bool allowSelf = false,
        CancellationToken cancellationToken = default);
}
