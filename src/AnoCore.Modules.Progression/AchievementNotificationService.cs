using System.Globalization;
using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Modules.Progression;

public sealed class AchievementNotificationService : IDisposable
{
    public static readonly PlayerSettingKey<bool> Preference = new("progression.notifications", true);
    private readonly IPlayerRegistry _players;
    private readonly IPlayerSettingsService _settings;
    private readonly IMessageService _messages;
    private readonly Action<Exception>? _reportError;
    private readonly IDisposable _toggle;
    private int _disposed;

    public AchievementNotificationService(IPlayerRegistry players, IPlayerSettingsService settings,
        IPlayerToggleCatalog toggles, IMessageService messages, Action<Exception>? reportError = null)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        ArgumentNullException.ThrowIfNull(toggles);
        _reportError = reportError;
        _toggle = toggles.Register(new ModuleId("ano.progression"),
            new PlayerToggleSetting(Preference, "Progression notifications",
                "Show newly unlocked permanent achievement tiers and awarded XP in chat."));
    }

    // Only newly committed records belong here. Chat delivery is best effort and never replays rewards.
    public async ValueTask NotifyAsync(PlayerSnapshot player, string name,
        IReadOnlyList<AchievementUnlockRecord> unlocked, CancellationToken cancellationToken = default)
    {
        if (unlocked.Count == 0 || !Current(player)) return;
        try
        {
            if (!await _settings.GetAsync(player.Id, Preference, cancellationToken).ConfigureAwait(false)
                || !Current(player)) return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { Report(exception); return; }

        var safeName = new string(name.Where(character => !char.IsControl(character)
            && character is not '{' and not '}' and not '|').Take(48).ToArray());
        foreach (var record in unlocked)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Current(player)) return;
            try
            {
                await _messages.SendAsync(new MessageRequest(
                    MessageTarget.ForPlayer(player.Id, player.SessionId), MessageChannel.Chat,
                    $"[ANO] Achievement unlocked: {safeName}, tier {record.Tier}. +{record.Grant.AwardedXp.ToString(CultureInfo.InvariantCulture)} XP."),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { Report(exception); }
        }
    }

    private bool Current(PlayerSnapshot player)
        => Volatile.Read(ref _disposed) == 0 && _players.TryGet(player.Id, out var current)
            && current is { IsConnected: true } && current.SessionId == player.SessionId;

    private void Report(Exception exception)
    {
        try { _reportError?.Invoke(exception); }
        catch { /* Diagnostics must not affect durable progression. */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _toggle.Dispose();
    }
}
