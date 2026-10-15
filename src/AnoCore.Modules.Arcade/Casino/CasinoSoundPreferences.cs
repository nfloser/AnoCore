using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Modules.Arcade.Casino;

/// <summary>One persistent on/off preference for the quiet, player-only reel click.</summary>
public sealed class CasinoSoundPreferences : IDisposable
{
    public const float ReelClickVolume = 0.08f;
    public const string ReelClickEvent = "UI.ButtonClick";
    public static readonly PlayerSettingKey<bool> Key = new("casino.reel_sound", true);

    private static readonly ModuleId Owner = new("ano.casino");
    private readonly IPlayerRegistry _players;
    private readonly IPlayerSettingsService _settings;
    private readonly object _gate = new();
    private readonly Dictionary<PlayerId, (PlayerSessionId Session, bool Enabled)> _loaded = [];
    private readonly List<IDisposable> _registrations = [];
    private bool _disposed;

    public CasinoSoundPreferences(IAnoCommandRegistry commands, IPlayerToggleCatalog catalog,
        IPlayerSettingsService settings, IPlayerRegistry players, IAnoEventBus events)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(events);
        try
        {
            _registrations.Add(catalog.Register(Owner, new PlayerToggleSetting(
                Key, "Casino-Walzensound", "Leiser Klick beim Stoppen jeder Walze (an/aus).")));
            _registrations.Add(commands.Register(Owner,
                new CommandDescriptor("casinosound", "Switch your quiet casino reel click on or off."),
                ToggleAsync));
            _registrations.Add(events.Subscribe<PlayerDisconnectedEvent>((e, _) =>
            {
                Forget(e.Player); return ValueTask.CompletedTask;
            }));
            _registrations.Add(events.Subscribe<PlayerReconnectedEvent>((e, _) =>
            {
                Forget(e.Previous); return ValueTask.CompletedTask;
            }));
            _registrations.Add(events.Subscribe<PlayerSettingChangedEvent>(async (e, token) =>
            {
                if (e.SettingName == Key.Name)
                    await RefreshAsync(e.Player, token).ConfigureAwait(false);
            }));
        }
        catch
        {
            foreach (var registration in _registrations) registration.Dispose();
            throw;
        }
    }

    public async ValueTask RefreshAsync(PlayerId playerId, CancellationToken token = default)
    {
        if (!_players.TryGet(playerId, out var player) || player is not { IsConnected: true })
            return;
        var enabled = await _settings.GetAsync(playerId, Key, token).ConfigureAwait(false);
        if (!_players.TryGet(playerId, out var current) || current is not { IsConnected: true }
            || current.SessionId != player.SessionId)
            return;
        lock (_gate)
        {
            if (!_disposed) _loaded[playerId] = (player.SessionId, enabled);
        }
    }

    /// <summary>Fail silent until the saved preference is loaded; never emit for an old session.</summary>
    public bool IsEnabled(PlayerId playerId)
    {
        if (!_players.TryGet(playerId, out var player) || player is not { IsConnected: true })
            return false;
        lock (_gate)
            return !_disposed && _loaded.TryGetValue(playerId, out var entry)
                && entry.Session == player.SessionId && entry.Enabled;
    }

    private async ValueTask<CommandResult> ToggleAsync(CommandContext context)
    {
        if (context.Caller is not { } id
            || !_players.TryGet(id, out var player) || player is not { IsConnected: true })
            return CommandResult.Fail(CommandFailureReason.Forbidden, "Nur für verbundene Spieler.");
        if (context.Arguments.Count != 0)
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "Verwendung: !casinosound");
        var wasEnabled = await _settings.GetAsync(id, Key, context.CancellationToken).ConfigureAwait(false);
        if (!_players.TryGet(id, out var current) || current is not { IsConnected: true }
            || current.SessionId != player.SessionId)
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "Spielersitzung geändert.");
        await _settings.SetAsync(id, Key, !wasEnabled, context.CancellationToken).ConfigureAwait(false);
        if (!_players.TryGet(id, out current) || current is not { IsConnected: true }
            || current.SessionId != player.SessionId)
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "Spielersitzung geändert.");
        lock (_gate)
            if (!_disposed) _loaded[id] = (player.SessionId, !wasEnabled);
        return CommandResult.Ok(!wasEnabled
            ? "[ANO] Casino-Walzensound: AN (immer sehr leise)."
            : "[ANO] Casino-Walzensound: AUS.");
    }

    private void Forget(PlayerSnapshot player)
    {
        lock (_gate)
            if (_loaded.TryGetValue(player.Id, out var current)
                && current.Session == player.SessionId)
                _loaded.Remove(player.Id);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _loaded.Clear();
        }
        foreach (var registration in _registrations) registration.Dispose();
    }
}
