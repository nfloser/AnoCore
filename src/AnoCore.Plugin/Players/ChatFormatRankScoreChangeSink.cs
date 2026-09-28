using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using AnoCore.Modules.Stats;

namespace AnoCore.Plugin.Players;

public sealed class ChatFormatRankScoreChangeSink(
    IPlayerRegistry players,
    Func<ChatFormatSnapshotLifecycle?> snapshots)
    : IRankScoreChangeSink
{
    private readonly IPlayerRegistry _players =
        players ?? throw new ArgumentNullException(nameof(players));
    private readonly Func<ChatFormatSnapshotLifecycle?> _snapshots =
        snapshots ?? throw new ArgumentNullException(nameof(snapshots));

    public ValueTask ScoreChangedAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lifecycle = _snapshots();
        if (lifecycle is null
            || !_players.TryGet(playerId, out var player)
            || player is null
            || !player.IsConnected)
        {
            return ValueTask.CompletedTask;
        }

        return lifecycle.RefreshAsync(player, cancellationToken);
    }
}
