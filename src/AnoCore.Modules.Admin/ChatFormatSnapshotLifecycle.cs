using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Modules.Admin;

public sealed class ChatFormatSnapshotLifecycle : IDisposable
{
    private readonly object _sync = new();
    private readonly ChatMessageFormatter _formatter;
    private readonly Action<Exception>? _onFailure;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IDisposable[] _subscriptions;
    private readonly Dictionary<PlayerId, PlayerSessionId> _sessions = [];
    private readonly Dictionary<PlayerId, SnapshotEntry> _snapshots = [];
    private int _disposed;

    public ChatFormatSnapshotLifecycle(
        IAnoEventBus events,
        ChatMessageFormatter formatter,
        Action<Exception>? onFailure = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        _onFailure = onFailure;
        _subscriptions =
        [
            events.Subscribe<PlayerConnectedEvent>(
                (value, token) => WarmAsync(value.Player, true, token)),
            events.Subscribe<PlayerReconnectedEvent>(
                (value, token) => WarmAsync(value.Current, true, token)),
            events.Subscribe<PlayerUpdatedEvent>(OnUpdatedAsync),
            events.Subscribe<PlayerDisconnectedEvent>(OnDisconnectedAsync),
        ];
    }

    public bool TryFormat(
        PlayerId playerId,
        PlayerSessionId sessionId,
        string? message,
        bool isTeamMessage,
        out string? formatted)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(sessionId);
        lock (_sync)
        {
            if (Volatile.Read(ref _disposed) == 0
                && _snapshots.TryGetValue(playerId, out var entry)
                && entry.SessionId == sessionId)
            {
                formatted = entry.Format.Format(message, isTeamMessage);
                return true;
            }
        }

        formatted = null;
        return false;
    }

    public ValueTask RefreshAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        return WarmAsync(player, false, cancellationToken);
    }

    public async ValueTask WarmExistingAsync(
        IEnumerable<PlayerSnapshot> players,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(players);
        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await WarmAsync(player, true, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _onFailure?.Invoke(exception);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        _lifetime.Cancel();
        lock (_sync)
        {
            _sessions.Clear();
            _snapshots.Clear();
        }

        _lifetime.Dispose();
    }

    private ValueTask OnUpdatedAsync(
        PlayerUpdatedEvent @event,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (@event.Previous.Name == @event.Current.Name)
            return ValueTask.CompletedTask;
        return WarmAsync(@event.Current, false, cancellationToken);
    }

    private ValueTask OnDisconnectedAsync(
        PlayerDisconnectedEvent @event,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_sessions.TryGetValue(@event.Player.Id, out var current)
                && current == @event.Player.SessionId)
            {
                _sessions.Remove(@event.Player.Id);
                _snapshots.Remove(@event.Player.Id);
            }
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask WarmAsync(
        PlayerSnapshot player,
        bool replaceSession,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!player.IsConnected)
            return;
        lock (_sync)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            if (!replaceSession
                && (!_sessions.TryGetValue(player.Id, out var current)
                    || current != player.SessionId))
                return;
            _sessions[player.Id] = player.SessionId;
            _snapshots.Remove(player.Id);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token);
        try
        {
            var prepared = await _formatter.PrepareAsync(player, linked.Token)
                .ConfigureAwait(false);
            lock (_sync)
            {
                if (Volatile.Read(ref _disposed) == 0
                    && _sessions.TryGetValue(player.Id, out var current)
                    && current == player.SessionId)
                {
                    _snapshots[player.Id] =
                        new SnapshotEntry(player.SessionId, prepared);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                if (_sessions.TryGetValue(player.Id, out var current)
                    && current == player.SessionId)
                    _snapshots.Remove(player.Id);
            }

            _onFailure?.Invoke(exception);
        }
    }

    private sealed record SnapshotEntry(
        PlayerSessionId SessionId,
        PreparedChatFormat Format);
}
