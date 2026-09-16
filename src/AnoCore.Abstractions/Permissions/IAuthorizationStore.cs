namespace AnoCore.Abstractions.Permissions;

public interface IAuthorizationStore
{
    ValueTask<AuthorizationState?> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(AuthorizationState state, CancellationToken cancellationToken = default);
}
