using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Runtime.Players;

public sealed class PlayerRegistry : IPlayerRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<PlayerId, PlayerSnapshot> _players = [];
    private readonly IAnoEventBus _events;

    public PlayerRegistry(IAnoEventBus events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public IReadOnlyCollection<PlayerSnapshot> OnlinePlayers
    {
        get
        {
            lock (_sync)
            {
                return _players.Values.ToArray();
            }
        }
    }

    public bool TryGet(PlayerId id, out PlayerSnapshot? player)
    {
        ArgumentNullException.ThrowIfNull(id);

        lock (_sync)
        {
            return _players.TryGetValue(id, out player);
        }
    }

    public async ValueTask<PlayerSnapshot> ConnectAsync(
        PlayerConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();

        var current = new PlayerSnapshot(
            connection.Id,
            PlayerSessionId.New(),
            connection.Name,
            isConnected: true,
            connection.IsAlive,
            connection.Team,
            connection.ConnectedAtUtc,
            connection.ConnectedAtUtc);

        PlayerSnapshot? previous;
        lock (_sync)
        {
            _players.TryGetValue(connection.Id, out previous);
            _players[connection.Id] = current;
        }

        if (previous is null)
        {
            await _events.PublishAsync(new PlayerConnectedEvent(current), cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await _events.PublishAsync(new PlayerReconnectedEvent(previous, current), cancellationToken)
                .ConfigureAwait(false);
        }

        return current;
    }

    public async ValueTask<PlayerSnapshot?> UpdateAsync(
        PlayerStateUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        cancellationToken.ThrowIfCancellationRequested();

        PlayerSnapshot previous;
        PlayerSnapshot current;

        lock (_sync)
        {
            if (!_players.TryGetValue(update.Id, out previous!)
                || previous.SessionId != update.SessionId)
            {
                return null;
            }

            if (update.UpdatedAtUtc < previous.LastUpdatedAtUtc)
            {
                return null;
            }

            current = new PlayerSnapshot(
                previous.Id,
                previous.SessionId,
                update.Name ?? previous.Name,
                isConnected: true,
                update.IsAlive ?? previous.IsAlive,
                update.Team ?? previous.Team,
                previous.ConnectedAtUtc,
                update.UpdatedAtUtc);

            _players[update.Id] = current;
        }

        await _events.PublishAsync(new PlayerUpdatedEvent(previous, current), cancellationToken)
            .ConfigureAwait(false);

        return current;
    }

    public async ValueTask<PlayerSnapshot?> DisconnectAsync(
        PlayerId id,
        PlayerSessionId sessionId,
        DateTimeOffset disconnectedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(sessionId);
        cancellationToken.ThrowIfCancellationRequested();

        PlayerSnapshot current;
        PlayerSnapshot disconnected;

        lock (_sync)
        {
            if (!_players.TryGetValue(id, out current!)
                || current.SessionId != sessionId)
            {
                return null;
            }

            var effectiveTimestamp = disconnectedAtUtc < current.LastUpdatedAtUtc
                ? current.LastUpdatedAtUtc
                : disconnectedAtUtc;

            disconnected = new PlayerSnapshot(
                current.Id,
                current.SessionId,
                current.Name,
                isConnected: false,
                current.IsAlive,
                current.Team,
                current.ConnectedAtUtc,
                effectiveTimestamp);

            _players.Remove(id);
        }

        await _events.PublishAsync(new PlayerDisconnectedEvent(disconnected), cancellationToken)
            .ConfigureAwait(false);

        return disconnected;
    }
}
