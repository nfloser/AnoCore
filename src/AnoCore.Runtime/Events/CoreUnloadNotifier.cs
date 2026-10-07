using AnoCore.Abstractions.Events;

namespace AnoCore.Runtime.Events;

public sealed class CoreUnloadNotifier
{
    private readonly IAnoEventBus _events;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<Exception>? _reportError;
    private int _notified;

    public CoreUnloadNotifier(IAnoEventBus events, Func<DateTimeOffset>? clock = null,
        Action<Exception>? reportError = null)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _reportError = reportError;
    }

    public async ValueTask NotifyAsync(bool hotReload)
    {
        if (Interlocked.Exchange(ref _notified, 1) != 0) return;
        try
        {
            await _events.PublishAsync(new CoreUnloadingEvent(hotReload, _clock().ToUniversalTime())).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            try { _reportError?.Invoke(exception); }
            catch { }
        }
    }
}
