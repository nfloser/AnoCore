using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Modules.Tournament;

public interface ITournamentTeamTransport
{
    ValueTask SetTeamAsync(
        PlayerSnapshot player,
        PlayerTeam team,
        CancellationToken cancellationToken = default);
}

public sealed class TournamentTeamEnforcement : IDisposable
{
    private readonly object _gate = new();
    private readonly IPlayerRegistry _players;
    private readonly ITournamentTeamAssignmentSource _assignments;
    private readonly ITournamentTeamTransport _transport;
    private readonly Action<Exception, PlayerSnapshot>? _onError;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly HashSet<PlayerSessionId> _inFlight = [];
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public TournamentTeamEnforcement(
        IAnoEventBus events,
        IPlayerRegistry players,
        ITournamentTeamAssignmentSource assignments,
        ITournamentTeamTransport transport,
        Action<Exception, PlayerSnapshot>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _assignments = assignments ?? throw new ArgumentNullException(nameof(assignments));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _onError = onError;

        try
        {
            _subscriptions.Add(events.Subscribe<PlayerConnectedEvent>(
                (value, token) => EnforceSafeAsync(value.Player, token)));
            _subscriptions.Add(events.Subscribe<PlayerReconnectedEvent>(
                (value, token) => EnforceSafeAsync(value.Current, token)));
            _subscriptions.Add(events.Subscribe<PlayerUpdatedEvent>(
                (value, token) => EnforceSafeAsync(value.Current, token)));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public async ValueTask ReconcileOnlineAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        foreach (var player in _players.OnlinePlayers.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnforceSafeAsync(player, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask EnforceSafeAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        try
        {
            await EnforceAsync(player, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested
            || _lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _onError?.Invoke(exception, player);
        }
    }

    private async ValueTask EnforceAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken)
    {
        if (!player.IsConnected
            || !_assignments.TryGetAssignedSide(player.Id, out var assigned)
            || player.Team == assigned
            || !IsCurrent(player))
        {
            return;
        }

        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0
                || !_inFlight.Add(player.SessionId))
            {
                return;
            }
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetime.Token);
            linked.Token.ThrowIfCancellationRequested();

            if (!IsCurrent(player))
                return;

            await _transport.SetTeamAsync(player, assigned, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _inFlight.Remove(player.SessionId);
            }
        }
    }

    private bool IsCurrent(PlayerSnapshot expected)
        => _players.TryGet(expected.Id, out var current)
            && current is { IsConnected: true }
            && current.SessionId == expected.SessionId;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _lifetime.Cancel();
        for (var index = _subscriptions.Count - 1; index >= 0; index--)
            _subscriptions[index].Dispose();
        _subscriptions.Clear();

        lock (_gate)
        {
            _inFlight.Clear();
        }

        _lifetime.Dispose();
    }
}
