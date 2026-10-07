using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class LiveRankScoringService : IDisposable
{
    private readonly LiveRankPolicy _policy;
    private readonly RankConfiguration _configuration;
    private readonly RankScoreWeights _weights;
    private readonly IRankPointEventRepository _events;
    private readonly IGameplayRankScoreRepository _scores;
    private readonly IPlayerRegistry _players;
    private readonly IPermissionEvaluator _permissions;
    private readonly IRankTransitionNotificationSink _notifications;
    private readonly IRankScoreChangeSink? _scoreChanges;
    private readonly Action<Exception>? _reportError;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<PlayerId, Streak> _streaks = [];
    private string? _firstBloodRound;
    private int _disposed;

    public LiveRankScoringService(RankConfiguration configuration, IRankPointEventRepository events,
        IGameplayRankScoreRepository scores, IPlayerRegistry players, IPermissionEvaluator permissions,
        IRankTransitionNotificationSink notifications, IRankScoreChangeSink? scoreChanges = null,
        Action<Exception>? reportError = null)
    {
        _policy = new LiveRankPolicy(configuration);
        if (configuration.Source != RankScoreSource.EventLedger)
            throw new ArgumentException("Live rank scoring requires the event-ledger score source.", nameof(configuration));
        _weights = configuration.ScoreWeights;
        _configuration = new RankConfiguration
        {
            Source = configuration.Source, StartingPoints = configuration.StartingPoints,
            Thresholds = configuration.Thresholds.ToList(), NotifyRankChanges = configuration.NotifyRankChanges,
        };
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _scores = scores ?? throw new ArgumentNullException(nameof(scores));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _scoreChanges = scoreChanges;
        _reportError = reportError;
    }

    public LiveRankPolicy Policy => _policy;

    public async ValueTask RecordDeathAsync(RankDeathInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Specials);
        var captured = input with { Specials = Array.AsReadOnly(input.Specials.Take(9).ToArray()) };
        _ = _policy.Death(captured, 0, 0, [], 1);
        using var linked = Link(cancellationToken);
        var token = linked.Token;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (await _events.ReadAsync(captured.Context.EventId, token).ConfigureAwait(false) is not null) return;
            PruneStreaks();
            if (!_policy.Allowed(captured.Context))
            {
                if (captured.Victim.Player is { } ineligibleVictim) _streaks.Remove(ineligibleVictim.Id);
                return;
            }
            var participants = Snapshots(captured);
            var previous = await ReadPointsAsync(participants, token).ConfigureAwait(false);
            var vips = await ReadVipsAsync(participants, token).ConfigureAwait(false);
            var validKill = _policy.ValidKill(captured);
            var streakCount = 1;
            if (validKill && captured.Attacker?.Player is { } attacker
                && _streaks.TryGetValue(attacker.Id, out var streak)
                && streak.Session == attacker.SessionId && streak.RoundKey == captured.Context.RoundKey
                && captured.Context.OccurredAtUtc >= streak.At
                && captured.Context.OccurredAtUtc - streak.At < _policy.StreakWindow)
                streakCount = Math.Min(65, streak.Count + 1);
            if (validKill && _firstBloodRound != captured.Context.RoundKey
                && !captured.Specials.Contains(GameplayStatKind.FirstBlood))
                captured = captured with { Specials = captured.Specials.Append(GameplayStatKind.FirstBlood).ToArray() };
            var awards = _policy.Death(captured,
                captured.Victim.Player is { } victim ? previous[victim.Id] : 0,
                captured.Attacker?.Player is { } killer ? previous[killer.Id] : 0, vips, streakCount);
            if (awards.Count == 0) return;
            var result = await _events.ApplyAsync(RankPointEventBatch.Create(captured.Context.EventId,
                "combat.death", captured.Context.OccurredAtUtc, awards), token).ConfigureAwait(false);
            if (!result.Applied) return;
            if (captured.Victim.Player is { } dead) _streaks.Remove(dead.Id);
            if (validKill)
            {
                _firstBloodRound = captured.Context.RoundKey;
                if (captured.Attacker?.Player is { } alive)
                    _streaks[alive.Id] = new(alive.SessionId, captured.Context.RoundKey, captured.Context.OccurredAtUtc, streakCount);
            }
            await PresentAsync(result.Batch, participants, previous).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask RecordGameplayAsync(GameplayStatEvent statistic, RankLiveContext context,
        PlayerSnapshot player, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statistic);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(player);
        if (statistic.PlayerId != player.Id || statistic.EventId != context.EventId || statistic.Amount != 1)
            throw new ArgumentException("Live rank gameplay events require one captured player/event occurrence.", nameof(statistic));
        if (!_policy.Allowed(context) || player.Team is not (PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist)
            || LiveRankPolicy.IsKillSpecial(statistic.Kind) || statistic.Kind == GameplayStatKind.FlashAssist) return;
        using var linked = Link(cancellationToken);
        var token = linked.Token;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (await _events.ReadAsync(statistic.EventId, token).ConfigureAwait(false) is not null) return;
            PlayerSnapshot[] participants = [player];
            var previous = await ReadPointsAsync(participants, token).ConfigureAwait(false);
            var vip = (await ReadVipsAsync(participants, token).ConfigureAwait(false)).Contains(player.Id);
            var points = _policy.Gameplay(statistic.Kind, vip);
            if (points == 0) return;
            var result = await _events.ApplyAsync(RankPointEventBatch.Create(statistic.EventId,
                "gameplay." + statistic.Kind, statistic.OccurredAtUtc, [new(player.Id, points)]), token).ConfigureAwait(false);
            if (result.Applied) await PresentAsync(result.Batch, participants, previous).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private CancellationTokenSource Link(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
    }

    private async ValueTask<Dictionary<PlayerId, long>> ReadPointsAsync(IReadOnlyList<PlayerSnapshot> participants, CancellationToken token)
    {
        var result = new Dictionary<PlayerId, long>();
        foreach (var player in participants)
            result[player.Id] = (await _scores.GetScorePlacementAsync(player.Id, _weights, token).ConfigureAwait(false))?.Points ?? 0;
        return result;
    }

    private async ValueTask<HashSet<PlayerId>> ReadVipsAsync(IReadOnlyList<PlayerSnapshot> participants, CancellationToken token)
    {
        var vips = new HashSet<PlayerId>();
        if (!_policy.UsesVip) return vips;
        foreach (var player in participants)
        {
            if (!Current(player)) continue;
            try
            {
                if (await _permissions.HasPermissionAsync(player.Id, _policy.VipPermission, token).ConfigureAwait(false)
                    && Current(player)) vips.Add(player.Id);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) { Report(exception); }
        }
        return vips;
    }

    private async ValueTask PresentAsync(RankPointEventBatch batch, IReadOnlyList<PlayerSnapshot> participants,
        IReadOnlyDictionary<PlayerId, long> previous)
    {
        foreach (var award in batch.Awards)
        {
            var player = participants.First(value => value.Id == award.PlayerId);
            if (!Current(player)) continue;
            if (_scoreChanges is not null)
            {
                try { await _scoreChanges.ScoreChangedAsync(player.Id, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception exception) { Report(exception); }
            }
            if (!_configuration.NotifyRankChanges || !Current(player)) continue;
            try
            {
                var current = (await _scores.GetScorePlacementAsync(player.Id, _weights, CancellationToken.None).ConfigureAwait(false))?.Points ?? 0;
                var transition = RankTransitionEvaluator.Evaluate(_configuration, previous[player.Id], current);
                if (transition is not null && Current(player))
                    await _notifications.NotifyAsync(player.Id, transition, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) { Report(exception); }
        }
    }

    private bool Current(PlayerSnapshot player) => Volatile.Read(ref _disposed) == 0
        && _players.TryGet(player.Id, out var current) && current is { IsConnected: true } && current.SessionId == player.SessionId;

    private void PruneStreaks()
    {
        foreach (var id in _streaks.Keys.Where(id => !_players.TryGet(id, out var current) || current is not { IsConnected: true }).ToArray())
            _streaks.Remove(id);
    }

    private static IReadOnlyList<PlayerSnapshot> Snapshots(RankDeathInput input)
        => new[] { input.Victim.Player, input.Attacker?.Player, input.Assister }.OfType<PlayerSnapshot>()
            .DistinctBy(player => player.Id).ToArray();

    private void Report(Exception exception)
    {
        try { _reportError?.Invoke(exception); }
        catch { /* Diagnostics cannot change committed rank events. */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        if (_notifications is IDisposable disposable) disposable.Dispose();
    }

    private sealed record Streak(PlayerSessionId Session, string RoundKey, DateTimeOffset At, int Count);
}
