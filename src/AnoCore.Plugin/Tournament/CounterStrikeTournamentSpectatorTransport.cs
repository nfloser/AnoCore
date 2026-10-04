using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using AnoCore.Modules.Tournament;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;

namespace AnoCore.Plugin.Tournament;

public sealed class CounterStrikeTournamentSpectatorTransport
    : ITournamentSpectatorTransport
{
    private const string RejectionReason =
        "Tournament spectator access is not allowed.";

    private readonly IPlayerRegistry _players;
    private readonly IPlayerDisconnectAction _disconnect;

    public CounterStrikeTournamentSpectatorTransport(
        IPlayerRegistry players,
        IPlayerDisconnectAction disconnect)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _disconnect = disconnect ?? throw new ArgumentNullException(nameof(disconnect));
    }

    public async ValueTask MoveToSpectatorAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        cancellationToken.ThrowIfCancellationRequested();

        await Server.NextWorldUpdateAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_players.TryGet(player.Id, out var current)
                || current is null
                || !current.IsConnected
                || current.SessionId != player.SessionId)
            {
                throw new InvalidOperationException(
                    "The tournament spectator session changed before team placement ran.");
            }

            var controller = Utilities.GetPlayers().FirstOrDefault(value =>
                value is { IsValid: true, IsBot: false, IsHLTV: false }
                && value.SteamID == player.Id.SteamId64)
                ?? throw new InvalidOperationException(
                    "The tournament spectator no longer has a valid controller.");

            controller.SwitchTeam(CsTeam.Spectator);
        }).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask RejectUnauthorizedAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default)
        => _disconnect.DisconnectAsync(
            player, RejectionReason, cancellationToken);
}
