using AnoCore.Abstractions.Players;
using AnoCore.Modules.Tournament;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;

namespace AnoCore.Plugin.Tournament;

public sealed class CounterStrikeTournamentTeamTransport : ITournamentTeamTransport
{
    private readonly IPlayerRegistry _players;

    public CounterStrikeTournamentTeamTransport(IPlayerRegistry players)
        => _players = players ?? throw new ArgumentNullException(nameof(players));

    public ValueTask SetTeamAsync(
        PlayerSnapshot player,
        PlayerTeam team,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (team is not (PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist))
            throw new ArgumentOutOfRangeException(nameof(team));
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Server.NextWorldUpdate(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_players.TryGet(player.Id, out var current)
                    || current is null
                    || !current.IsConnected
                    || current.SessionId != player.SessionId)
                {
                    throw new InvalidOperationException(
                        "The tournament player session changed before team enforcement ran.");
                }

                var controller = Utilities.GetPlayers().FirstOrDefault(value =>
                    value is { IsValid: true, IsBot: false, IsHLTV: false }
                    && value.SteamID == player.Id.SteamId64)
                    ?? throw new InvalidOperationException(
                        "The tournament player no longer has a valid controller.");

                controller.SwitchTeam(team == PlayerTeam.Terrorist
                    ? CsTeam.Terrorist
                    : CsTeam.CounterTerrorist);
                completion.TrySetResult(true);
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        return new ValueTask(completion.Task);
    }
}
