using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;

namespace AnoCore.Plugin.Commands;

public sealed class CounterStrikeWarningNotifier : IWarningNotifier
{
    private readonly IPlayerRegistry _players;
    private readonly ILogger _logger;
    private readonly Func<bool> _isActive;

    public CounterStrikeWarningNotifier(IPlayerRegistry players, ILogger logger, Func<bool> isActive)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
    }

    public void Notify(PlayerId targetId, PlayerSessionId sessionId, string message)
    {
        try
        {
            Server.NextWorldUpdate(() =>
            {
                try
                {
                    if (!_isActive()
                        || !_players.TryGet(targetId, out var snapshot)
                        || snapshot is null
                        || !snapshot.IsConnected
                        || snapshot.SessionId != sessionId)
                        return;

                    var player = Utilities.GetPlayers()
                        .FirstOrDefault(value => value.IsValid && value.SteamID == targetId.SteamId64);
                    player?.PrintToChat(message);
                }
                catch (Exception error)
                {
                    _logger.LogError(error, "Warning notification failed for {TargetId}.", targetId);
                }
            });
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Could not schedule warning notification for {TargetId}.", targetId);
        }
    }
}
