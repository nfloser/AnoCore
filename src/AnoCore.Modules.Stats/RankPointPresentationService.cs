using System.Globalization;
using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Modules.Stats;

public sealed record RankPointChange(Guid EventId, string Source, PlayerSnapshot Player,
    string RoundKey, long PreviousPoints, long CurrentPoints);

public interface IRankPointEventSink
{
    ValueTask CommittedAsync(RankPointChange change, CancellationToken cancellationToken = default);
    ValueTask CompleteRoundAsync(string roundKey, CancellationToken cancellationToken = default);
}

public sealed class RankPointPresentationService : IRankPointEventSink, IDisposable
{
    public static PlayerSettingKey<bool> PointSetting { get; } = new("rank.point-notifications", true);
    public static PlayerSettingKey<bool> SummarySetting { get; } = new("rank.round-summaries", true);
    private readonly bool _pointsEnabled;
    private readonly bool _summariesEnabled;
    private readonly IPlayerRegistry _players;
    private readonly IPlayerSettingsService _settings;
    private readonly IMessageService _messages;
    private readonly Action<Exception>? _reportError;
    private readonly List<IDisposable> _registrations = [];
    private readonly object _gate = new();
    private readonly HashSet<(Guid Event, PlayerId Player, PlayerSessionId Session)> _seen = [];
    private readonly Queue<(Guid Event, PlayerId Player, PlayerSessionId Session)> _seenOrder = [];
    private readonly Dictionary<(PlayerId Player, PlayerSessionId Session, string Round), Summary> _rounds = [];
    private readonly HashSet<string> _completed = new(StringComparer.Ordinal);
    private readonly Queue<string> _completedOrder = [];
    private int _disposed;

    public RankPointPresentationService(bool pointNotices, bool roundSummaries, IPlayerRegistry players,
        IPlayerSettingsService settings, IPlayerToggleCatalog toggles, IMessageService messages,
        Action<Exception>? reportError = null)
    {
        _pointsEnabled = pointNotices;
        _summariesEnabled = roundSummaries;
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _reportError = reportError;
        ArgumentNullException.ThrowIfNull(toggles);
        try
        {
            var owner = new ModuleId("ano.ranks");
            _registrations.Add(toggles.Register(owner, new PlayerToggleSetting(PointSetting,
                "Rank point notices", "Show individual committed rank point changes.")));
            _registrations.Add(toggles.Register(owner, new PlayerToggleSetting(SummarySetting,
                "Rank round summaries", "Show the net rank point change for a completed round.")));
        }
        catch { Dispose(); throw; }
    }

    public async ValueTask CommittedAsync(RankPointChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(change.Player);
        ValidateRound(change.RoundKey);
        if (change.EventId == Guid.Empty || change.PreviousPoints < 0 || change.CurrentPoints < 0
            || string.IsNullOrWhiteSpace(change.Source) || change.Source.Length > 64 || change.Source.Any(char.IsControl))
            throw new ArgumentException("Rank point presentation input is invalid.", nameof(change));
        if ((!_pointsEnabled && !_summariesEnabled) || !Current(change.Player)) return;
        var delta = checked(change.CurrentPoints - change.PreviousPoints);
        if (delta == 0) return;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            var seenKey = (change.EventId, change.Player.Id, change.Player.SessionId);
            if (!_seen.Add(seenKey)) return;
            _seenOrder.Enqueue(seenKey);
            while (_seenOrder.Count > 512) _seen.Remove(_seenOrder.Dequeue());
            foreach (var key in _rounds.Keys.Where(key => !_players.TryGet(key.Player, out var current)
                         || current is not { IsConnected: true } || current.SessionId != key.Session).ToArray())
                _rounds.Remove(key);
            var roundKey = (change.Player.Id, change.Player.SessionId, change.RoundKey);
            if (_summariesEnabled && !_completed.Contains(change.RoundKey)
                && (_rounds.ContainsKey(roundKey) || _rounds.Count < 128))
            {
                var previous = _rounds.GetValueOrDefault(roundKey)?.Delta ?? 0;
                _rounds[roundKey] = new(change.Player, checked(previous + delta));
            }
        }
        if (_pointsEnabled)
            await SendAsync(change.Player, PointSetting,
                $"[ANO] Rank points: {Signed(delta)} ({change.Source}); total {change.CurrentPoints.ToString(CultureInfo.InvariantCulture)}.",
                cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask CompleteRoundAsync(string roundKey, CancellationToken cancellationToken = default)
    {
        ValidateRound(roundKey);
        Summary[] entries;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0 || !_completed.Add(roundKey)) return;
            _completedOrder.Enqueue(roundKey);
            while (_completedOrder.Count > 128) _completed.Remove(_completedOrder.Dequeue());
            var keys = _rounds.Keys.Where(key => key.Round == roundKey).ToArray();
            entries = keys.Select(key => _rounds[key]).OrderBy(entry => entry.Player.Id.SteamId64).ToArray();
            foreach (var key in keys) _rounds.Remove(key);
        }
        foreach (var entry in entries)
            if (entry.Delta != 0)
                await SendAsync(entry.Player, SummarySetting, $"[ANO] Round rank points: {Signed(entry.Delta)}.",
                    cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SendAsync(PlayerSnapshot player, PlayerSettingKey<bool> setting, string text, CancellationToken token)
    {
        if (!Current(player)) return;
        try
        {
            var enabled = await _settings.GetAsync(player.Id, setting, token).ConfigureAwait(false);
            if (enabled && Current(player))
                await _messages.SendAsync(new MessageRequest(MessageTarget.ForPlayer(player.Id, player.SessionId),
                    MessageChannel.Chat, text), token).ConfigureAwait(false);
        }
        catch (Exception exception) { Report(exception); }
    }

    private bool Current(PlayerSnapshot player) => Volatile.Read(ref _disposed) == 0
        && _players.TryGet(player.Id, out var current) && current is { IsConnected: true } && current.SessionId == player.SessionId;

    private static string Signed(long value) => (value > 0 ? "+" : "") + value.ToString(CultureInfo.InvariantCulture);

    private static void ValidateRound(string roundKey)
    {
        if (string.IsNullOrWhiteSpace(roundKey) || roundKey.Length > 192 || roundKey.Any(char.IsControl))
            throw new ArgumentException("A bounded round identity is required.", nameof(roundKey));
    }

    private void Report(Exception exception)
    {
        try { _reportError?.Invoke(exception); }
        catch { /* Presentation diagnostics cannot affect committed awards. */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var registration in _registrations) registration.Dispose();
        lock (_gate)
        {
            _rounds.Clear();
            _seen.Clear();
            _seenOrder.Clear();
            _completed.Clear();
            _completedOrder.Clear();
        }
    }

    private sealed record Summary(PlayerSnapshot Player, long Delta);
}
