using System.Globalization;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class PlaytimeNotificationConfiguration
{
    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 300;

    public static IReadOnlyCollection<string> Validate(PlaytimeNotificationConfiguration value)
        => value is null
            ? ["Playtime notification configuration is required."]
            : value.IntervalSeconds is < 30 or > 86400
                ? ["Playtime notification interval must be between 30 and 86400 seconds."]
                : [];
}

public sealed class PlaytimeNotificationService : IDisposable
{
    public static readonly PlayerSettingKey<bool> Preference = new("playtime.notifications", true);
    private readonly object _sync = new();
    private readonly IPlayerRegistry _players;
    private readonly IPlaytimeRepository _repository;
    private readonly IPlayerSettingsService _settings;
    private readonly IMessageService _messages;
    private readonly bool _enabled;
    private readonly TimeSpan _interval;
    private readonly Action<Exception>? _onFailure;
    private readonly IDisposable _toggle;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly Dictionary<PlayerId, Schedule> _schedules = [];
    private int _busy;
    private int _disposed;

    private PlaytimeNotificationService(
        PlaytimeNotificationConfiguration configuration,
        IPlayerRegistry players,
        IPlaytimeRepository repository,
        IPlayerSettingsService settings,
        IPlayerToggleCatalog catalog,
        IMessageService messages,
        Action<Exception>? onFailure)
    {
        _players = players;
        _repository = repository;
        _settings = settings;
        _messages = messages;
        _enabled = configuration.Enabled;
        _interval = TimeSpan.FromSeconds(configuration.IntervalSeconds);
        _onFailure = onFailure;
        _lifetimeToken = _lifetime.Token;
        try
        {
            _toggle = catalog.Register(new ModuleId("ano.stats.playtime"),
                new PlayerToggleSetting(Preference, "Playtime notifications",
                    "Periodically show your total recorded playtime in chat."));
        }
        catch
        {
            _lifetime.Dispose();
            throw;
        }
    }

    public static async Task<PlaytimeNotificationService> CreateAsync(
        IConfigStore configuration,
        IPlayerRegistry players,
        IPlaytimeRepository repository,
        IPlayerSettingsService settings,
        IPlayerToggleCatalog catalog,
        IMessageService messages,
        Action<Exception>? onFailure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(messages);
        var loaded = await configuration.LoadAsync("playtime-notifications",
            () => new PlaytimeNotificationConfiguration(),
            PlaytimeNotificationConfiguration.Validate, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new PlaytimeNotificationService(
            loaded, players, repository, settings, catalog, messages, onFailure);
    }

    // Called only after the durable playtime checkpoint has completed.
    public async ValueTask TickAsync(DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_enabled || Volatile.Read(ref _disposed) != 0
            || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetimeToken);
            var online = _players.OnlinePlayers.ToArray();
            lock (_sync)
            {
                var ids = online.Select(player => player.Id).ToHashSet();
                foreach (var id in _schedules.Keys.Where(id => !ids.Contains(id)).ToArray())
                    _schedules.Remove(id);
            }

            foreach (var player in online)
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return;
                linked.Token.ThrowIfCancellationRequested();
                if (!IsCurrent(player) || !TryReserve(player, now))
                    continue;
                try
                {
                    if (!await _settings.GetAsync(player.Id, Preference, linked.Token)
                        .ConfigureAwait(false) || !IsCurrent(player))
                        continue;
                    var totals = await _repository.ReadAsync(player.Id,
                        DateOnly.FromDateTime(now.UtcDateTime), linked.Token).ConfigureAwait(false);
                    if (!IsCurrent(player))
                        continue;
                    await _messages.SendAsync(new MessageRequest(
                        MessageTarget.ForPlayer(player.Id, player.SessionId),
                        MessageChannel.Chat,
                        $"[ANO] Total playtime: {totals.Total.ToString("c", CultureInfo.InvariantCulture)}."),
                        linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    ReportFailure(exception);
                }
            }
        }
        catch (OperationCanceledException) when (
            _lifetimeToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private bool IsCurrent(PlayerSnapshot player)
        => Volatile.Read(ref _disposed) == 0
            && _players.TryGet(player.Id, out var current)
            && current is { IsConnected: true }
            && current.SessionId == player.SessionId;

    private bool TryReserve(PlayerSnapshot player, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return false;
            var previous = _schedules.TryGetValue(player.Id, out var schedule)
                && schedule.Session == player.SessionId
                ? schedule.LastAttempt : player.ConnectedAtUtc;
            if (now < previous)
            {
                _schedules[player.Id] = new Schedule(player.SessionId, now);
                return false;
            }
            if (now - previous < _interval)
                return false;
            // Failed or disabled attempts are also bounded to one per interval.
            _schedules[player.Id] = new Schedule(player.SessionId, now);
            return true;
        }
    }

    private void ReportFailure(Exception exception)
    {
        try
        {
            _onFailure?.Invoke(exception);
        }
        catch
        {
            // Diagnostic failures must not affect other players or durable tracking.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _lifetime.Cancel();
        _toggle.Dispose();
        lock (_sync)
            _schedules.Clear();
        _lifetime.Dispose();
    }

    private sealed record Schedule(PlayerSessionId Session, DateTimeOffset LastAttempt);
}
