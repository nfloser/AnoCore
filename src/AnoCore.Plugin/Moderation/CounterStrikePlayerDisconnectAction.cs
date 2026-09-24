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
        linked.Token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = linked.Token.Register(() => completion.TrySetCanceled(linked.Token));

        Server.NextWorldUpdate(() =>
        {
            if (linked.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (!_players.TryGet(player.Id, out var current)
                    || current is null
                    || !current.IsConnected
                    || current.SessionId != player.SessionId)
                {
                    completion.TrySetException(new InvalidOperationException(
                        "The player session changed before the disconnect."));
                    return;
                }

                var controller = Utilities.GetPlayers()
                    .FirstOrDefault(value =>
                        value is { IsValid: true, IsBot: false, IsHLTV: false }
                        && value.SteamID == player.Id.SteamId64);
                if (controller is null)
                {
                    completion.TrySetException(new InvalidOperationException(
                        "The player controller is no longer available."));
                    return;
                }

                controller.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        await completion.Task.ConfigureAwait(false);
    }
}
