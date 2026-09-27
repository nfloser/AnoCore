using AnoCore.Abstractions.Players;
using AnoCore.Modules.Stats;
using CounterStrikeSharp.API;

namespace AnoCore.Plugin.Players;

public sealed class CounterStrikeRankTransitionNotifier(
    IPlayerRegistry players) : IRankTransitionNotificationSink
{
    private readonly IPlayerRegistry _players =
        players ?? throw new ArgumentNullException(nameof(players));

    public ValueTask NotifyAsync(PlayerId playerId, RankTransition transition,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_players.TryGet(playerId, out var expected)
            || expected is null || !expected.IsConnected)
            return ValueTask.CompletedTask;

        var expectedSession = expected.SessionId;
        var direction = transition.Kind == RankTransitionKind.Promotion
            ? "Promotion" : "Demotion";
        var message = $"[ANO] Rank {direction}: {transition.Previous.Name} -> "
            + $"{transition.Current.Name} ({transition.PreviousPoints} -> "
            + $"{transition.CurrentPoints} points).";

        Server.NextWorldUpdate(() =>
        {
            if (!_players.TryGet(playerId, out var current)
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
}
