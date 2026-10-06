using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class GameplayRankTransitionMonitor : IDisposable
{
    private readonly RankConfiguration _configuration;
    private readonly ICombatRepository _combat;
    private readonly IGameplayStatRepository _gameplay;
    private readonly IRankTransitionNotificationSink _notifications;
    private readonly IRankScoreChangeSink? _scoreChanges;
    private readonly Action<Exception>? _reportError;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _disposed;

    public GameplayRankTransitionMonitor(RankConfiguration configuration,
        ICombatRepository combat, IGameplayStatRepository gameplay,
        IRankTransitionNotificationSink notifications,
        IRankScoreChangeSink? scoreChanges = null, Action<Exception>? reportError = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _gameplay = gameplay ?? throw new ArgumentNullException(nameof(gameplay));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _scoreChanges = scoreChanges;
        _reportError = reportError;
        var errors = RankConfiguration.Validate(configuration);
        if (errors.Count != 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(configuration));
        RankScoreQueries.ValidateRepository(combat, configuration);
    }

    public async ValueTask RecordAsync(GameplayStatEvent statistic,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statistic);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            long? previous = null;
            var weighted = _configuration.GameplayPoints.GetValueOrDefault(statistic.Kind) != 0;
            if (weighted && _configuration.NotifyRankChanges)
            {
                try
                {
                    previous = (await RankScoreQueries.PlacementAsync(_combat, _configuration,
                        statistic.PlayerId, cancellationToken).ConfigureAwait(false))?.Points ?? 0;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Report(exception);
                }
            }

            await _gameplay.RecordAsync(statistic, cancellationToken).ConfigureAwait(false);
            // Once persisted, presentation failures cannot turn an ingestion success into a retry.
            if (!weighted || Volatile.Read(ref _disposed) != 0) return;
            if (_scoreChanges is not null)
            {
                try
                {
                    await _scoreChanges.ScoreChangedAsync(statistic.PlayerId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Report(exception);
                }
            }
            if (previous is not null)
            {
                try
                {
                    var current = (await RankScoreQueries.PlacementAsync(_combat, _configuration,
                        statistic.PlayerId, CancellationToken.None).ConfigureAwait(false))?.Points ?? 0;
                    var transition = RankTransitionEvaluator.Evaluate(_configuration, previous.Value, current);
                    if (transition is not null && Volatile.Read(ref _disposed) == 0)
                        await _notifications.NotifyAsync(statistic.PlayerId, transition, CancellationToken.None)
                            .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Report(exception);
                }
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _notifications is IDisposable disposable)
            disposable.Dispose();
    }

    private void Report(Exception exception)
    {
        try
        {
            _reportError?.Invoke(exception);
        }
        catch
        {
            // Diagnostics must not change durable ingestion results.
        }
    }
}
