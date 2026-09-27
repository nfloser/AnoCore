using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class RankAdjustmentNotificationService
    : IRankAdjustmentAdministrationService, IDisposable
{
    private readonly RankConfiguration _configuration;
    private readonly IRankAdjustmentAdministrationService _inner;
    private readonly ICombatRepository _combat;
    private readonly IRankTransitionNotificationSink _notifications;
    private readonly Action<Exception>? _reportError;
    private int _disposed;

    public RankAdjustmentNotificationService(
        RankConfiguration configuration,
        IRankAdjustmentAdministrationService inner,
        ICombatRepository combat,
        IRankTransitionNotificationSink notifications,
        Action<Exception>? reportError = null)
    {
        _configuration = configuration
            ?? throw new ArgumentNullException(nameof(configuration));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _notifications = notifications
            ?? throw new ArgumentNullException(nameof(notifications));
        _reportError = reportError;
        var errors = RankConfiguration.Validate(configuration);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(configuration));
    }

    public async ValueTask<RankAdjustmentAdminResult> ApplyAsync(
        RankAdjustmentAdminOperation operation,
        PlayerId targetId,
        long points,
        PlayerId? actorId,
        string reason,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var result = await _inner.ApplyAsync(operation, targetId, points, actorId,
            reason, occurredAtUtc, cancellationToken).ConfigureAwait(false);
        if (!_configuration.NotifyAdministrativeRankChanges)
            return result;

        try
        {
            var totals = await _combat.ReadAsync(targetId, CancellationToken.None)
                .ConfigureAwait(false);
            var combatPoints = _configuration.Score(totals);
            var previousPoints = Adjust(combatPoints, result.PreviousPoints);
            var currentPoints = Adjust(combatPoints, result.CurrentPoints);
            var transition = RankTransitionEvaluator.Evaluate(
                _configuration, previousPoints, currentPoints);
            if (transition is not null)
                await _notifications.NotifyAsync(
                    targetId, transition, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Report(exception);
        }

        return result;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0
            && _notifications is IDisposable disposable)
            disposable.Dispose();
    }

    private static long Adjust(long combatPoints, long adjustment)
        => Math.Max(0, checked(combatPoints + adjustment));

    private void Report(Exception exception)
    {
        try
        {
            _reportError?.Invoke(exception);
        }
        catch
        {
            // A diagnostic callback must not change the committed command result.
        }
    }
}
