using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<RankScoreboardMode>))]
public enum RankScoreboardMode { Disabled, Premier, Competitive, Wingman, DangerZone }

public sealed class RankScoreboardConfiguration
{
    public bool SyncScore { get; set; }
    public RankScoreboardMode RankMode { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Enabled => SyncScore || RankMode != RankScoreboardMode.Disabled;
}

public sealed record RankScoreboardBadge(int Ranking, sbyte Type, int Wins);
public sealed record RankScoreboardProjection(int? Score, RankScoreboardBadge? Badge)
{
    public static RankScoreboardProjection Create(RankConfiguration configuration, long points)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (points < 0) throw new ArgumentOutOfRangeException(nameof(points));
        var bounded = (int)Math.Min(int.MaxValue, points);
        var ordinal = configuration.Thresholds.Count(threshold => points >= threshold.MinimumPoints);
        var badge = configuration.Scoreboard.RankMode switch
        {
            RankScoreboardMode.Disabled => null,
            RankScoreboardMode.Premier => new RankScoreboardBadge(bounded, 11, 10),
            RankScoreboardMode.Competitive => new RankScoreboardBadge(Math.Min(18, ordinal), 12, 10),
            RankScoreboardMode.Wingman => new RankScoreboardBadge(Math.Min(18, ordinal), 7, 10),
            RankScoreboardMode.DangerZone => new RankScoreboardBadge(Math.Min(15, ordinal), 10, 10),
            _ => throw new ArgumentOutOfRangeException(nameof(configuration)),
        };
        return new(configuration.Scoreboard.SyncScore ? bounded : null, badge);
    }
}

// A changed external value relinquishes ownership for the rest of this session.
public sealed class OwnedRankScoreboardValue<T>
{
    private bool _owned;
    private bool _released;
    private T? _original;
    private T? _last;
    public bool TryApply(T current, T desired)
    {
        if (_released) return false;
        if (_owned && !EqualityComparer<T>.Default.Equals(current, _last))
        {
            _owned = false;
            _released = true;
            return false;
        }
        if (!_owned) _original = current;
        _last = desired;
        _owned = true;
        return true;
    }
    public bool TryRestore(T current, out T original)
    {
        original = _original!;
        var restore = _owned && EqualityComparer<T>.Default.Equals(current, _last);
        _owned = false;
        _released = true;
        return restore;
    }
}

public interface IRankScoreboardTransport : IDisposable
{
    ValueTask ApplyAsync(PlayerSnapshot player, RankScoreboardProjection projection, CancellationToken cancellationToken = default);
}

public sealed class RankScoreboardService : IDisposable
{
    private readonly RankConfiguration _configuration;
    private readonly RankScoreWeights _weights;
    private readonly IPlayerRegistry _players;
    private readonly IGameplayRankScoreRepository _scores;
    private readonly IRankScoreboardTransport _transport;
    private readonly Action<Exception>? _reportError;
    private readonly CancellationTokenSource _lifetime = new();
    private int _busy;
    private int _disposed;

    public RankScoreboardService(RankConfiguration configuration, IPlayerRegistry players,
        IGameplayRankScoreRepository scores, IRankScoreboardTransport transport, Action<Exception>? reportError = null)
    {
        var errors = RankConfiguration.Validate(configuration);
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors), nameof(configuration));
        _weights = configuration.ScoreWeights;
        _configuration = new RankConfiguration
        {
            Thresholds = configuration.Thresholds.ToList(),
            Scoreboard = new() { SyncScore = configuration.Scoreboard.SyncScore, RankMode = configuration.Scoreboard.RankMode },
        };
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _scores = scores ?? throw new ArgumentNullException(nameof(scores));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _reportError = reportError;
    }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!_configuration.Scoreboard.Enabled || Volatile.Read(ref _disposed) != 0
            || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            foreach (var player in _players.OnlinePlayers.Take(64).ToArray())
            {
                linked.Token.ThrowIfCancellationRequested();
                try
                {
                    var entry = await _scores.GetScorePlacementAsync(player.Id, _weights, linked.Token).ConfigureAwait(false);
                    if (Volatile.Read(ref _disposed) == 0 && _players.TryGet(player.Id, out var current)
                        && current is { IsConnected: true } && current.SessionId == player.SessionId)
                        await _transport.ApplyAsync(player, RankScoreboardProjection.Create(_configuration, entry?.Points ?? 0), linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    try { _reportError?.Invoke(exception); }
                    catch { /* Diagnostics cannot block other players. */ }
                }
            }
        }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _transport.Dispose();
    }
}
