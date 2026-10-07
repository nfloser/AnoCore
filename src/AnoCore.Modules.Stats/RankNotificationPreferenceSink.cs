using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Modules.Stats;

public sealed class RankNotificationPreferenceSink : ISessionRankTransitionNotificationSink, IDisposable
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

    public ValueTask NotifyAsync(PlayerId playerId, RankTransition transition,
        CancellationToken cancellationToken = default)
        => NotifyCoreAsync(playerId, transition, null, cancellationToken);

    public ValueTask NotifyAsync(PlayerSnapshot player, RankTransition transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        return NotifyCoreAsync(player.Id, transition, player, cancellationToken);
    }

    private async ValueTask NotifyCoreAsync(PlayerId playerId, RankTransition transition,
        PlayerSnapshot? expected, CancellationToken cancellationToken)
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

        if (enabled && Volatile.Read(ref _disposed) == 0)
        {
            if (expected is not null && _inner is ISessionRankTransitionNotificationSink pinned)
                await pinned.NotifyAsync(expected, transition, cancellationToken).ConfigureAwait(false);
            else
                await _inner.NotifyAsync(playerId, transition, cancellationToken).ConfigureAwait(false);
        }
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
