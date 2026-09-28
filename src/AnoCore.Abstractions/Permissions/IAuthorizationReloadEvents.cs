namespace AnoCore.Abstractions.Permissions;

public interface IAuthorizationReloadEvents
{
    event Action? Reloaded;
}
