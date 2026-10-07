using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Stats;

// Samples only consecutive eligible native checkpoints. Historical totals are never backfilled.
public sealed class RankPlaytimeService : IDisposable
{
    private readonly LiveRankPolicy _policy;
    private readonly Func<PlayerSnapshot, RankLiveContext, CancellationToken, ValueTask> _award;
    private readonly Action<Exception>? _reportError;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<PlayerId, Schedule> _schedules = [];
    private int _disposed;

    public RankPlaytimeService(LiveRankPolicy policy,
        Func<PlayerSnapshot, RankLiveContext, CancellationToken, ValueTask> award,
        Action<Exception>? reportError = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _award = award ?? throw new ArgumentNullException(nameof(award));
        _reportError = reportError;
    }

    public async ValueTask TickAsync(IReadOnlyList<PlayerSnapshot> players, RankLiveContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(context);
        if (_policy.PlaytimeInterval == TimeSpan.Zero || Volatile.Read(ref _disposed) != 0) return;
        if (players.Count > 64 || players.Any(player => player is null)
            || players.Select(player => player.Id).Distinct().Count() != players.Count)
            throw new ArgumentException("At most 64 unique captured players are supported.", nameof(players));
        var captured = players.ToArray();
        var allowed = _policy.Allowed(context);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var online = captured.Where(player => player.IsConnected).Select(player => player.Id).ToHashSet();
            foreach (var id in _schedules.Keys.Where(id => !online.Contains(id)).ToArray()) _schedules.Remove(id);
            foreach (var player in captured.Where(player => player.IsConnected))
            {
                linked.Token.ThrowIfCancellationRequested();
                var eligible = allowed && player.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist;
                if (!_schedules.TryGetValue(player.Id, out var schedule) || schedule.Session != player.SessionId)
                    _schedules[player.Id] = schedule = new(player.SessionId, context.OccurredAtUtc, eligible);
                else
                {
                    if (context.OccurredAtUtc < schedule.At) continue;
                    var elapsed = context.OccurredAtUtc - schedule.At;
                    // A delayed checkpoint contributes at most one normal five-second sample.
                    if (eligible && schedule.Eligible && elapsed > TimeSpan.Zero)
                        schedule.Accrued += elapsed < TimeSpan.FromSeconds(5) ? elapsed : TimeSpan.FromSeconds(5);
                    schedule.At = context.OccurredAtUtc;
                    schedule.Eligible = eligible;
                }
                if (schedule.Pending is null && eligible && schedule.Accrued >= _policy.PlaytimeInterval)
                    {
                    schedule.Pending = context with { EventId = Guid.NewGuid() };
                    schedule.PendingPlayer = player;
                }
                if (schedule.Pending is null) continue;
                try
                {
                    await _award(schedule.PendingPlayer!, schedule.Pending, linked.Token).ConfigureAwait(false);
                    schedule.Accrued -= _policy.PlaytimeInterval;
                    schedule.Pending = null;
                    schedule.PendingPlayer = null;
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    // One frozen pending award per session; retries do not mint another identity.
                    schedule.Accrued = schedule.Accrued > _policy.PlaytimeInterval + TimeSpan.FromSeconds(5)
                        ? _policy.PlaytimeInterval + TimeSpan.FromSeconds(5) : schedule.Accrued;
                    try { _reportError?.Invoke(exception); }
                    catch { /* Diagnostics cannot prevent other player checkpoints. */ }
                }
            }
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
    }

    private sealed class Schedule(PlayerSessionId session, DateTimeOffset at, bool eligible)
    {
        public PlayerSessionId Session { get; } = session;
        public DateTimeOffset At { get; set; } = at;
        public bool Eligible { get; set; } = eligible;
        public TimeSpan Accrued { get; set; }
        public RankLiveContext? Pending { get; set; }
        public PlayerSnapshot? PendingPlayer { get; set; }
    }
}
