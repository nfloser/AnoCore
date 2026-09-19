using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Modules.Admin;

public sealed class ConnectBanEnforcement : IDisposable
{
    public const string DefaultDisconnectReason = "You are banned from this server.";

    private readonly IModerationService _moderation;
    private readonly IPlayerDisconnectAction _disconnect;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private readonly HashSet<PlayerSessionId> _disconnecting = [];
    private readonly List<IDisposable> _subscriptions = [];
    private int _disposed;

    public ConnectBanEnforcement(
        IAnoEventBus events,
        IModerationService moderation,
        IPlayerDisconnectAction disconnect,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        _moderation = moderation ?? throw new ArgumentNullException(nameof(moderation));
        _disconnect = disconnect ?? throw new ArgumentNullException(nameof(disconnect));
        _timeProvider = timeProvider ?? TimeProvider.System;

        _subscriptions.Add(events.Subscribe<PlayerConnectedEvent>(
            (value, token) => CheckAsync(value.Player, token)));
        _subscriptions.Add(events.Subscribe<PlayerReconnectedEvent>(
            (value, token) => CheckAsync(value.Current, token)));
        _subscriptions.Add(events.Subscribe<PlayerDisconnectedEvent>(
            (value, _) =>
            {
                Release(value.Player.SessionId);
                return ValueTask.CompletedTask;
            }));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        for (var index = _subscriptions.Count - 1; index >= 0; index--)
        {
            _subscriptions[index].Dispose();
        }

        _subscriptions.Clear();
        lock (_gate)
        {
            _disconnecting.Clear();
        }
    }

    private void Release(PlayerSessionId sessionId)
    {
        lock (_gate)
        {
            _disconnecting.Remove(sessionId);
        }
    }

    public async ValueTask CheckAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        var state = await _moderation.GetStateAsync(
            player.Id,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);

        if (!state.Restrictions.HasFlag(ModerationRestriction.Connect))
        {
            return;
        }

        lock (_gate)
        {
            if (!_disconnecting.Add(player.SessionId))
            {
                return;
            }
        }

        try
        {
            await _disconnect.DisconnectAsync(
                player,
                DefaultDisconnectReason,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
            {
                _disconnecting.Remove(player.SessionId);
            }

            throw;
        }
    }
}
