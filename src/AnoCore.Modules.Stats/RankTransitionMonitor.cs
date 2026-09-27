using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public interface IRankTransitionNotificationSink
{
    ValueTask NotifyAsync(PlayerId playerId, RankTransition transition,
        CancellationToken cancellationToken = default);
}

public sealed class RankTransitionMonitor : IDisposable
{
    private readonly RankConfiguration _configuration;
    private readonly ICombatRepository _repository;
    private readonly IRankTransitionNotificationSink _notifications;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _disposed;

    public RankTransitionMonitor(RankConfiguration configuration,
        ICombatRepository repository, IRankTransitionNotificationSink notifications)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        var errors = RankConfiguration.Validate(configuration);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(configuration));
    }

    public async ValueTask RecordAsync(CombatDeath death,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(death);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_configuration.NotifyRankChanges)
        {
            await _repository.RecordAsync(death, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var affected = AffectedPlayers(death);
            var previous = new Dictionary<PlayerId, long>(affected.Count);
            foreach (var playerId in affected)
                previous[playerId] = await ReadPointsAsync(playerId, cancellationToken)
                    .ConfigureAwait(false);

            await _repository.RecordAsync(death, cancellationToken).ConfigureAwait(false);

            foreach (var playerId in affected)
            {
                var current = await ReadPointsAsync(playerId, cancellationToken)
                    .ConfigureAwait(false);
                var transition = RankTransitionEvaluator.Evaluate(
                    _configuration, previous[playerId], current);
                if (transition is not null)
                    await _notifications.NotifyAsync(playerId, transition, cancellationToken)
                        .ConfigureAwait(false);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0
            && _notifications is IDisposable disposable)
            disposable.Dispose();
    }

    private async ValueTask<long> ReadPointsAsync(PlayerId playerId,
        CancellationToken cancellationToken)
    {
        var score = await _repository.GetScorePlacementAsync(playerId,
            _configuration.KillPoints, _configuration.AssistPoints,
            _configuration.DeathPenalty, cancellationToken).ConfigureAwait(false);
        return score?.Points ?? 0;
    }

    private static IReadOnlyList<PlayerId> AffectedPlayers(CombatDeath death)
    {
        var players = new List<PlayerId>(3) { death.VictimId };
        if (death.AttackerId is not null) players.Add(death.AttackerId);
        if (death.AssisterId is not null) players.Add(death.AssisterId);
        return players.Distinct().ToArray();
    }
}
