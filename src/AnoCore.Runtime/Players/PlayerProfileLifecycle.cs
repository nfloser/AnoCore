using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Runtime.Players;

public sealed class PlayerProfileLifecycle : IDisposable
{
    private readonly object _sync = new();
    private readonly IPlayerRepository _profiles;
    private readonly IPlayerRegistry _players;
    private readonly IAnoEventBus _events;
    private readonly Action<Exception>? _onEventFailure;
    private readonly HashSet<PlayerSessionId> _loadedSessions = [];
    private readonly List<IDisposable> _subscriptions = [];
    private int _started;
    private int _disposed;

    public PlayerProfileLifecycle(
        IPlayerRepository profiles,
        IPlayerRegistry players,
        IAnoEventBus events,
        Action<Exception>? onEventFailure = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _onEventFailure = onEventFailure;
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The player profile lifecycle has already started.");

        _subscriptions.Add(_events.Subscribe<PlayerConnectedEvent>(
            (value, token) => PersistLoadedAsync(value.Player, token)));
        _subscriptions.Add(_events.Subscribe<PlayerReconnectedEvent>(
            OnReconnectedAsync));
        _subscriptions.Add(_events.Subscribe<PlayerDisconnectedEvent>(
            (value, token) => PersistUnloadedAsync(value.Player, token)));
        _subscriptions.Add(_events.Subscribe<PlayerUpdatedEvent>(
            (value, token) => value.Previous.Name != value.Current.Name
                ? PersistAsync(value.Current, token)
                : ValueTask.CompletedTask));

        try
        {
            foreach (var player in _players.OnlinePlayers)
                await PersistLoadedAsync(player, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        for (var index = _subscriptions.Count - 1; index >= 0; index--)
            _subscriptions[index].Dispose();
        _subscriptions.Clear();
        lock (_sync)
            _loadedSessions.Clear();
    }

    private async ValueTask OnReconnectedAsync(
        PlayerReconnectedEvent value,
        CancellationToken cancellationToken)
    {
        await PublishCommittedAsync(
            new PlayerProfileUnloadedEvent(value.Previous, CreateProfile(value.Previous)))
            .ConfigureAwait(false);
        lock (_sync)
            _loadedSessions.Remove(value.Previous.SessionId);
        await PersistLoadedAsync(value.Current, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask PersistLoadedAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken)
    {
        await PersistAsync(player, cancellationToken).ConfigureAwait(false);
        if (!_players.TryGet(player.Id, out var current)
            || current?.SessionId != player.SessionId)
            return;

        lock (_sync)
        {
            if (!_loadedSessions.Add(player.SessionId))
                return;
        }

        await PublishCommittedAsync(
            new PlayerProfileLoadedEvent(player, CreateProfile(player))).ConfigureAwait(false);
    }

    private async ValueTask PersistUnloadedAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken)
    {
        await PersistAsync(player, cancellationToken).ConfigureAwait(false);
        lock (_sync)
            _loadedSessions.Remove(player.SessionId);
        await PublishCommittedAsync(
            new PlayerProfileUnloadedEvent(player, CreateProfile(player))).ConfigureAwait(false);
    }

    private ValueTask PersistAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken)
        => _profiles.UpsertAsync(CreateProfile(player), cancellationToken);

    private async ValueTask PublishCommittedAsync<TEvent>(TEvent value)
        where TEvent : IAnoEvent
    {
        try
        {
            await _events.PublishAsync(value, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            try
            {
                _onEventFailure?.Invoke(exception);
            }
            catch (Exception)
            {
                // Diagnostics cannot turn a committed profile write into a failure.
            }
        }
    }

    private static PlayerProfile CreateProfile(PlayerSnapshot player)
        => new(player.Id, player.Name, player.ConnectedAtUtc, player.LastUpdatedAtUtc);
}
