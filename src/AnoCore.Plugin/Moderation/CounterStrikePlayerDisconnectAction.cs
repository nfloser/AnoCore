using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.ValveConstants.Protobuf;

namespace AnoCore.Plugin.Moderation;

public sealed class CounterStrikePlayerDisconnectAction : IPlayerDisconnectAction
{
    private readonly IPlayerRegistry _players;
    private readonly CancellationToken _lifetime;

    public CounterStrikePlayerDisconnectAction(
        IPlayerRegistry players,
        CancellationToken lifetime = default)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _lifetime = lifetime;
    }

    public ValueTask DisconnectAsync(
        PlayerSnapshot player,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        cancellationToken.ThrowIfCancellationRequested();
        _lifetime.ThrowIfCancellationRequested();

        Server.NextWorldUpdate(() =>
        {
            if (cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested)
            {
                return;
            }

            if (!_players.TryGet(player.Id, out var current)
                || current is null
                || !current.IsConnected
                || current.SessionId != player.SessionId)
            {
                return;
            }

            var controller = Utilities.GetPlayers()
                .FirstOrDefault(value =>
                    value is { IsValid: true, IsBot: false, IsHLTV: false }
                    && value.SteamID == player.Id.SteamId64);

            controller?.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
        });

        return ValueTask.CompletedTask;
    }
}
