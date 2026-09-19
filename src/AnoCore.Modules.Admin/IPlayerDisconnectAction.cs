using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public interface IPlayerDisconnectAction
{
    ValueTask DisconnectAsync(
        PlayerSnapshot player,
        string reason,
        CancellationToken cancellationToken = default);
}
