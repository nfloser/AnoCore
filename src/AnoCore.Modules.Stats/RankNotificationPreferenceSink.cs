using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Modules.Stats;

public sealed class RankNotificationPreferenceSink : IRankTransitionNotificationSink, IDisposable
{
    public static PlayerSettingKey<bool> EnabledSetting { get; } =
        new("rank.notifications", true);

    private readonly IPlayerSettingsService _settings;
    private readonly IRankTransitionNotificationSink _inner;
    private readonly Action<Exception>? _reportError;
    private int _disposed;

    public RankNotificationPreferenceSink(
        IPlayerSettingsService settings,
        IRankTransitionNotificationSink inner,
        Action<Exception>? reportError = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _reportError = reportError;
    }

    public async ValueTask NotifyAsync(
        PlayerId playerId,
        RankTransition transition,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(transition);

        bool enabled;
        try
        {
            enabled = await _settings.GetAsync(
                playerId, EnabledSetting, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Report(exception);
            return;
        }

        if (enabled)
            await _inner.NotifyAsync(playerId, transition, cancellationToken)
                .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0
            && _inner is IDisposable disposable)
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
            // Diagnostics must never turn a presentation preference failure
            // into a gameplay failure.
        }
    }
}
