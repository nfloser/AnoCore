using AnoCore.Abstractions.Players;
using AnoCore.Modules.Stats;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace AnoCore.Plugin.Players;

public sealed class CounterStrikeRankScoreboardTransport(IPlayerRegistry players, Action<Exception>? reportError = null)
    : IRankScoreboardTransport
{
    private readonly Dictionary<PlayerId, Ownership> _owned = [];
    private int _disposed;

    public ValueTask ApplyAsync(PlayerSnapshot player, RankScoreboardProjection projection,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0) return ValueTask.CompletedTask;
        Server.NextWorldUpdate(() =>
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            try
            {
                var controller = Resolve(player);
                if (controller is null) return;
                foreach (var id in _owned.Keys.Where(id => !players.TryGet(id, out var current) || current is not { IsConnected: true }).ToArray())
                    _owned.Remove(id);
                if (!_owned.TryGetValue(player.Id, out var ownership) || ownership.Session != player.SessionId)
                    _owned[player.Id] = ownership = new(player.SessionId);
                if (projection.Score is { } score && ownership.Score.TryApply(controller.Score, score))
                {
                    controller.Score = score;
                    Utilities.SetStateChanged(controller, "CCSPlayerController", "m_iScore");
                }
                if (projection.Badge is { } badge && ownership.Badge.TryApply(ReadBadge(controller), badge))
                    WriteBadge(controller, badge);
            }
            catch (Exception exception) { Report(exception); }
        });
        return ValueTask.CompletedTask;
    }

    private CCSPlayerController? Resolve(PlayerSnapshot expected)
    {
        if (!players.TryGet(expected.Id, out var current) || current is not { IsConnected: true }
            || current.SessionId != expected.SessionId) return null;
        return Utilities.GetPlayers().FirstOrDefault(controller => controller is { IsValid: true, IsBot: false, IsHLTV: false }
            && controller.SteamID == expected.Id.SteamId64);
    }

    private static RankScoreboardBadge ReadBadge(CCSPlayerController controller)
        => new(controller.CompetitiveRanking, controller.CompetitiveRankType, controller.CompetitiveWins);

    private static void WriteBadge(CCSPlayerController controller, RankScoreboardBadge badge)
    {
        controller.CompetitiveRanking = badge.Ranking;
        controller.CompetitiveRankType = badge.Type;
        controller.CompetitiveWins = badge.Wins;
        Utilities.SetStateChanged(controller, "CCSPlayerController", "m_iCompetitiveRanking");
        Utilities.SetStateChanged(controller, "CCSPlayerController", "m_iCompetitiveRankType");
        Utilities.SetStateChanged(controller, "CCSPlayerController", "m_iCompetitiveWins");
    }

    // Composition disposes this transport on the native server thread.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var pair in _owned)
        {
            try
            {
                if (!players.TryGet(pair.Key, out var player) || player is null || player.SessionId != pair.Value.Session) continue;
                var controller = Resolve(player);
                if (controller is null) continue;
                if (pair.Value.Score.TryRestore(controller.Score, out var score))
                {
                    controller.Score = score;
                    Utilities.SetStateChanged(controller, "CCSPlayerController", "m_iScore");
                }
                if (pair.Value.Badge.TryRestore(ReadBadge(controller), out var badge)) WriteBadge(controller, badge);
            }
            catch (Exception exception) { Report(exception); }
        }
        _owned.Clear();
    }

    private void Report(Exception exception)
    {
        try { reportError?.Invoke(exception); }
        catch { /* Diagnostics cannot disrupt native cleanup. */ }
    }

    private sealed class Ownership(PlayerSessionId session)
    {
        public PlayerSessionId Session { get; } = session;
        public OwnedRankScoreboardValue<int> Score { get; } = new();
        public OwnedRankScoreboardValue<RankScoreboardBadge> Badge { get; } = new();
    }
}
