using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Modules.Admin;

public sealed class ModerationSnapshotLifecycle : IDisposable
{
    private readonly object _sync = new();
    private readonly IModerationService _moderation;
    private readonly IModerationSnapshotProvider _snapshots;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IDisposable[] _subscriptions;
    private readonly Dictionary<PlayerId, PlayerSessionId> _sessions = [];
    private int _disposed;

    public ModerationSnapshotLifecycle(
        IAnoEventBus events,
        IModerationService moderation,
        IModerationSnapshotProvider snapshots,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        _moderation = moderation ?? throw new ArgumentNullException(nameof(moderation));
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _timeProvider = timeProvider ?? TimeProvider.System;

        _subscriptions =
        [
            events.Subscribe<PlayerConnectedEvent>(OnConnectedAsync),
            events.Subscribe<PlayerReconnectedEvent>(OnReconnectedAsync),
            events.Subscribe<PlayerDisconnectedEvent>(OnDisconnectedAsync),
        ];
    }

    public async ValueTask WarmExistingAsync(
        IEnumerable<PlayerSnapshot> players,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(players);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        List<Exception>? failures = null;
        foreach (var player in players
                     .Where(value => value.IsConnected)
                     .OrderBy(value => value.Id.SteamId64))
        {
            try
            {
                await WarmAsync(
                        player,
                        invalidateFirst: false,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException(
                "One or more moderation snapshots could not be warmed.",
                failures);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _lifetime.Cancel();
        lock (_sync)
        {
            _sessions.Clear();
        }

    }

    private ValueTask OnConnectedAsync(
        PlayerConnectedEvent @event,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return WarmAsync(@event.Player, invalidateFirst: false, cancellationToken);
    }

    private ValueTask OnReconnectedAsync(
        PlayerReconnectedEvent @event,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return WarmAsync(@event.Current, invalidateFirst: true, cancellationToken);
    }

    private async ValueTask OnDisconnectedAsync(
        PlayerDisconnectedEvent @event,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (!TryForgetCurrentSession(@event.Player))
        {
            return;
        }

        using var linked = CreateLinkedToken(cancellationToken);
        try
        {
            await _snapshots.InvalidateAsync(@event.Player.Id, linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async ValueTask WarmAsync(
        PlayerSnapshot player,
        bool invalidateFirst,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(player);
        TrackCurrentSession(player);

        using var linked = CreateLinkedToken(cancellationToken);
        try
        {
            if (invalidateFirst)
            {
                await _snapshots.InvalidateAsync(player.Id, linked.Token)
                    .ConfigureAwait(false);
            }

            await _moderation.GetStateAsync(
                    player.Id,
                    _timeProvider.GetUtcNow().ToUniversalTime(),
                    linked.Token)
                .ConfigureAwait(false);

            if (!HasTrackedSession(player.Id))
            {
                await _snapshots.InvalidateAsync(player.Id, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private CancellationTokenSource CreateLinkedToken(CancellationToken cancellationToken)
        => CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);

    private void TrackCurrentSession(PlayerSnapshot player)
    {
        lock (_sync)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _sessions[player.Id] = player.SessionId;
            }
        }
    }

    private bool HasTrackedSession(PlayerId playerId)
    {
        lock (_sync)
        {
            return _sessions.ContainsKey(playerId);
        }
    }

    private bool TryForgetCurrentSession(PlayerSnapshot player)
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(player.Id, out var sessionId)
                || sessionId != player.SessionId)
            {
                return false;
            }

            _sessions.Remove(player.Id);
            return true;
        }
    }
}
