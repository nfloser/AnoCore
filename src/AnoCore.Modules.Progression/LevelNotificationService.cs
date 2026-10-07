using System.Globalization;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Modules.Progression;

public sealed class LevelNotificationService : IDisposable
{
    public static readonly PlayerSettingKey<bool> Preference = new("progression.level-notifications", true);
    private readonly IPlayerRegistry _players;
    private readonly IPlayerSettingsService _settings;
    private readonly IMessageService _messages;
    private readonly Action<Exception>? _reportError;
    private readonly IDisposable _toggle;
    private readonly IDisposable _subscription;
    private int _disposed;

    public LevelNotificationService(IAnoEventBus events, IPlayerRegistry players,
        IPlayerSettingsService settings, IPlayerToggleCatalog toggles, IMessageService messages,
        Action<Exception>? reportError = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(toggles);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _reportError = reportError;
        _toggle = toggles.Register(new ModuleId("ano.progression.levels"),
            new PlayerToggleSetting(Preference, "Level-up notifications", "Show newly earned lifetime levels in chat."));
        try { _subscription = events.Subscribe<ProgressionLevelUpEvent>(NotifyAsync); }
        catch { _toggle.Dispose(); throw; }
    }

    private async ValueTask NotifyAsync(ProgressionLevelUpEvent value, CancellationToken cancellationToken)
    {
        var player = value.Player;
        if (value.Grant.PlayerId != player.Id || value.Level <= value.PreviousLevel || !Current(player)) return;
        try
        {
            if (!await _settings.GetAsync(player.Id, Preference, cancellationToken).ConfigureAwait(false)
                || !Current(player)) return;
            cancellationToken.ThrowIfCancellationRequested();
            await _messages.SendAsync(new MessageRequest(
                MessageTarget.ForPlayer(player.Id, player.SessionId), MessageChannel.Chat,
                $"[ANO] Level up: {value.PreviousLevel.ToString(CultureInfo.InvariantCulture)} -> {value.Level.ToString(CultureInfo.InvariantCulture)}. Lifetime XP: {value.Grant.LifetimeXpAfter.ToString(CultureInfo.InvariantCulture)}."),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            try { _reportError?.Invoke(exception); }
            catch { /* Notification failures cannot roll back XP. */ }
        }
    }

    private bool Current(PlayerSnapshot player)
        => Volatile.Read(ref _disposed) == 0 && _players.TryGet(player.Id, out var current)
            && current is { IsConnected: true } && current.SessionId == player.SessionId;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _subscription.Dispose();
        _toggle.Dispose();
    }
}
