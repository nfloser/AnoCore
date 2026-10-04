using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Modules.Tournament;

public interface ITournamentSpectatorTransport
{
    ValueTask MoveToSpectatorAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default);

    ValueTask RejectUnauthorizedAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default);
}

public sealed class TournamentSpectatorEnforcement : IDisposable
{
    private readonly object _gate = new();
    private readonly IPlayerRegistry _players;
    private readonly ITournamentTeamAssignmentSource _assignments;
    private readonly ITournamentSpectatorPolicySource _policies;
    private readonly ITournamentSpectatorTransport _transport;
    private readonly Action<Exception, PlayerSnapshot>? _onError;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly HashSet<PlayerSessionId> _inFlight = [];
    private readonly Dictionary<PlayerSessionId, long> _rejected = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private int _disposed;

    public TournamentSpectatorEnforcement(
        IAnoEventBus events,
        IPlayerRegistry players,
        ITournamentTeamAssignmentSource assignments,
        ITournamentSpectatorPolicySource policies,
        ITournamentSpectatorTransport transport,
        Action<Exception, PlayerSnapshot>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _assignments = assignments ?? throw new ArgumentNullException(nameof(assignments));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _onError = onError;
        _lifetimeToken = _lifetime.Token;

        try
        {
            _subscriptions.Add(events.Subscribe<PlayerConnectedEvent>(
                (value, token) => EnforceSafeAsync(value.Player, token)));
            _subscriptions.Add(events.Subscribe<PlayerReconnectedEvent>(
                (value, token) => EnforceSafeAsync(value.Current, token)));
            _subscriptions.Add(events.Subscribe<PlayerUpdatedEvent>(
                (value, token) => EnforceSafeAsync(value.Current, token)));
            _subscriptions.Add(events.Subscribe<PlayerDisconnectedEvent>(
                (value, _) =>
                {
                    Release(value.Player.SessionId);
                    return ValueTask.CompletedTask;
                }));
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
            || _lifetimeToken.IsCancellationRequested)
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
        if (!player.IsConnected || !IsCurrent(player))
            return;

        var activeMatch = _assignments.ActiveMatchId;
        if (activeMatch is null)
            return;

        var snapshot = _policies.Read();
        var policy = snapshot.Policy;
        if (policy is null || policy.MatchId != activeMatch.Value)
            return;

        var decision = policy.Decide(player.Id);
        if (decision.IsRosterPlayer)
        {
            ReleaseRejected(player.SessionId);
            return;
        }

        var shouldMove = decision.CanSpectate && player.Team != PlayerTeam.Spectator;
        var shouldReject = !decision.CanSpectate
            && !WasRejected(player.SessionId, snapshot.Version);
        if (!shouldMove && !shouldReject)
            return;

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
                cancellationToken, _lifetimeToken, snapshot.Lifetime);
            linked.Token.ThrowIfCancellationRequested();

            if (!IsCurrent(player)
                || _assignments.ActiveMatchId != activeMatch
                || !PolicyStillCurrent(snapshot))
            {
                return;
            }

            if (shouldMove)
            {
                await _transport.MoveToSpectatorAsync(player, linked.Token)
                    .ConfigureAwait(false);
                ReleaseRejected(player.SessionId);
                return;
            }

            await _transport.RejectUnauthorizedAsync(player, linked.Token)
                .ConfigureAwait(false);
            lock (_gate)
            {
                if (IsCurrent(player)
                    && _assignments.ActiveMatchId == activeMatch
                    && PolicyStillCurrent(snapshot))
                {
                    _rejected[player.SessionId] = snapshot.Version;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _inFlight.Remove(player.SessionId);
            }
        }
    }

    private bool PolicyStillCurrent(TournamentSpectatorPolicySnapshot expected)
    {
        var current = _policies.Read();
        return current.Version == expected.Version
            && ReferenceEquals(current.Policy, expected.Policy);
    }

    private bool WasRejected(PlayerSessionId sessionId, long policyVersion)
    {
        lock (_gate)
        {
            return _rejected.TryGetValue(sessionId, out var rejectedVersion)
                && rejectedVersion == policyVersion;
        }
    }

    private void ReleaseRejected(PlayerSessionId sessionId)
    {
        lock (_gate) _rejected.Remove(sessionId);
    }

    private void Release(PlayerSessionId sessionId)
    {
        lock (_gate)
        {
            _inFlight.Remove(sessionId);
            _rejected.Remove(sessionId);
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
            _rejected.Clear();
        }

        _lifetime.Dispose();
    }
}
