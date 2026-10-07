using AnoCore.Abstractions.Players;
using AnoCore.Modules.Stats;
using CounterStrikeSharp.API;

namespace AnoCore.Plugin.Players;

public sealed class CounterStrikeRankTransitionNotifier(
    IPlayerRegistry players) : ISessionRankTransitionNotificationSink, IDisposable
{
    private readonly IPlayerRegistry _players =
        players ?? throw new ArgumentNullException(nameof(players));
    private int _disposed;

    public ValueTask NotifyAsync(PlayerId playerId, RankTransition transition,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0 || !_players.TryGet(playerId, out var expected)
            || expected is null || !expected.IsConnected)
            return ValueTask.CompletedTask;

        return NotifyAsync(expected, transition, cancellationToken);
    }

    public ValueTask NotifyAsync(PlayerSnapshot expected, RankTransition transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(transition);
        cancellationToken.ThrowIfCancellationRequested();
        var playerId = expected.Id;
        if (Volatile.Read(ref _disposed) != 0 || !_players.TryGet(playerId, out var currentSession)
            || currentSession is not { IsConnected: true } || currentSession.SessionId != expected.SessionId)
            return ValueTask.CompletedTask;
        var expectedSession = expected.SessionId;
        var direction = transition.Kind == RankTransitionKind.Promotion
            ? "Promotion" : "Demotion";
        var message = $"[ANO] Rank {direction}: {transition.Previous.Name} -> "
            + $"{transition.Current.Name} ({transition.PreviousPoints} -> "
            + $"{transition.CurrentPoints} points).";

        Server.NextWorldUpdate(() =>
        {
            if (Volatile.Read(ref _disposed) != 0 || !_players.TryGet(playerId, out var current)
                || current is null || !current.IsConnected
                || current.SessionId != expectedSession)
                return;
            var controller = Utilities.GetPlayers().FirstOrDefault(candidate =>
                candidate is { IsValid: true, IsBot: false, IsHLTV: false }
                && candidate.SteamID == playerId.SteamId64);
            controller?.PrintToChat(message);
        });
        return ValueTask.CompletedTask;
    }

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
