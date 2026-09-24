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

    public async ValueTask DisconnectAsync(
        PlayerSnapshot player,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();

        await Server.NextWorldUpdateAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!_players.TryGet(player.Id, out var current)
                || current is null
                || !current.IsConnected
                || current.SessionId != player.SessionId)
            {
                throw new InvalidOperationException(
                    "The player session changed before the disconnect.");
            }

            var controller = Utilities.GetPlayers()
                .FirstOrDefault(value =>
                    value is { IsValid: true, IsBot: false, IsHLTV: false }
                    && value.SteamID == player.Id.SteamId64);
            if (controller is null)
            {
                throw new InvalidOperationException(
                    "The player controller is no longer available.");
            }

            controller.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
        }).WaitAsync(token).ConfigureAwait(false);
    }
}
