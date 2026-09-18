using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;

namespace AnoCore.Plugin.Hud;

public sealed class CounterStrikeCustomHudService : ICustomHudService, IDisposable
{
    private readonly object _gate = new();
    private readonly BasePlugin _plugin;
    private readonly ILogger _logger;
    private readonly Dictionary<CustomHudId, Registration> _registrations = [];
    private readonly HashSet<Registration> _pendingCleanup = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _started;
    private bool _disposed;

    public CounterStrikeCustomHudService(BasePlugin plugin, ILogger logger)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _plugin.RegisterListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);
            _plugin.RegisterListener<Listeners.OnMapStart>(OnMapStart);
            _plugin.RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
            _started = true;
        }
    }

    public IDisposable Register(
        ModuleId owner,
        CustomHudDefinition definition,
        CustomHudClickHandler? clickHandler = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(definition);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_registrations.ContainsKey(definition.Id))
            {
                throw new InvalidOperationException($"Custom HUD '{definition.Id}' is already registered.");
            }

            var registration = new Registration(owner, definition, clickHandler);
            _registrations.Add(definition.Id, registration);
            return new RegistrationHandle(this, registration);
        }
    }

    public bool Show(PlayerId playerId, CustomHudId hudId)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(hudId);
        if (!TryGetRegistration(hudId, out var registration))
        {
            return false;
        }

        lock (_gate)
        {
            var state = registration.GetOrCreateState(playerId);
            state.Visible = true;
        }

        Schedule(() => ApplyPlayerState(registration, playerId));
        return true;
    }

    public bool Hide(PlayerId playerId, CustomHudId hudId)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(hudId);
        if (!TryGetRegistration(hudId, out var registration))
        {
            return false;
        }

        lock (_gate)
        {
            registration.GetOrCreateState(playerId).Visible = false;
        }

        Schedule(() => ApplyPlayerState(registration, playerId));
        return true;
    }

    public void HideAll(CustomHudId hudId)
    {
        ArgumentNullException.ThrowIfNull(hudId);
        if (!TryGetRegistration(hudId, out var registration))
        {
            return;
        }

        PlayerId[] players;
        lock (_gate)
        {
            players = registration.PlayerStates.Keys.ToArray();
            foreach (var state in registration.PlayerStates.Values)
            {
                state.Visible = false;
            }
        }

        Schedule(() =>
        {
            foreach (var playerId in players)
            {
                ApplyPlayerState(registration, playerId);
            }
        });
    }

    public bool SetText(
        PlayerId playerId,
        CustomHudId hudId,
        string panelId,
        string value,
        string variableName = "text")
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(hudId);
        var normalizedPanel = ValidateToken(panelId, nameof(panelId));
        var normalizedVariable = ValidateToken(variableName, nameof(variableName));
        if (!TryGetRegistration(hudId, out var registration))
        {
            return false;
        }

        lock (_gate)
        {
            registration.GetOrCreateState(playerId).Text[(normalizedPanel, normalizedVariable)] = value ?? string.Empty;
        }

        Schedule(() => ApplyText(registration, playerId, normalizedPanel, normalizedVariable));
        return true;
    }

    public bool SetClass(
        PlayerId playerId,
        CustomHudId hudId,
        string panelId,
        string className,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(hudId);
        var normalizedPanel = ValidateToken(panelId, nameof(panelId));
        var normalizedClass = ValidateToken(className, nameof(className));
        if (!TryGetRegistration(hudId, out var registration))
        {
            return false;
        }

        lock (_gate)
        {
            registration.GetOrCreateState(playerId).Classes[(normalizedPanel, normalizedClass)] = enabled;
        }

        Schedule(() => ApplyClass(registration, playerId, normalizedPanel, normalizedClass));
        return true;
    }

    public void Dispose()
    {
        Registration[] registrations;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _lifetime.Cancel();
            registrations = _registrations.Values
                .Concat(_pendingCleanup)
                .Distinct()
                .ToArray();
            _registrations.Clear();
            _pendingCleanup.Clear();

            if (_started)
            {
                _plugin.RemoveListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);
                _plugin.RemoveListener<Listeners.OnMapStart>(OnMapStart);
                _plugin.RemoveListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
                _started = false;
            }
        }

        foreach (var registration in registrations)
        {
            CleanupRegistration(registration);
        }

        _lifetime.Dispose();
    }

    private bool TryGetRegistration(CustomHudId hudId, out Registration registration)
    {
        lock (_gate)
        {
            if (!_disposed && _registrations.TryGetValue(hudId, out registration!))
            {
                return true;
            }
        }

        registration = null!;
        return false;
    }

    private void Schedule(Action action)
    {
        if (_disposed)
        {
            return;
        }

        Server.NextWorldUpdate(() =>
        {
            if (!_disposed)
            {
                action();
            }
        });
    }

    private void OnMapStart(string mapName)
    {
        _ = mapName;
        Registration[] registrations;
        lock (_gate)
        {
            registrations = _registrations.Values.ToArray();
            foreach (var registration in registrations)
            {
                registration.Entity = null;
                foreach (var state in registration.PlayerStates.Values)
                {
                    state.LastSlot = null;
                }
            }
        }

        Server.NextWorldUpdate(() =>
        {
            foreach (var registration in registrations)
            {
                if (!IsCurrent(registration))
                {
                    continue;
                }

                EnsureEntity(registration);
                foreach (var playerId in SnapshotVisiblePlayers(registration))
                {
                    ApplyPlayerState(registration, playerId);
                }
            }
        });
    }

    private void OnClientPutInServer(int playerSlot)
    {
        Server.NextWorldUpdate(() => ResetSlot(playerSlot, reapplyDesiredState: false));
        _plugin.AddTimer(
            1.5f,
            () => ResetSlot(playerSlot, reapplyDesiredState: true),
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void ResetSlot(int playerSlot, bool reapplyDesiredState)
    {
        var player = Utilities.GetPlayerFromSlot(playerSlot);
        if (player is null || !player.IsValid)
        {
            return;
        }

        Registration[] registrations;
        lock (_gate)
        {
            registrations = _registrations.Values.ToArray();
        }

        foreach (var registration in registrations)
        {
            if (!EnsureEntity(registration))
            {
                continue;
            }

            registration.Entity!.SetHasClassForPlayer(
                player,
                registration.Definition.RootPanelId,
                registration.Definition.VisibleClass,
                false);
            if (registration.Definition.CaptureInput)
            {
                registration.Entity!.SetInputCaptureEnabled(player, false);
            }
        }

        if (!reapplyDesiredState || player.SteamID == 0)
        {
            return;
        }

        PlayerId playerId;
        try
        {
            playerId = new PlayerId(player.SteamID);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        foreach (var registration in registrations)
        {
            ApplyPlayerState(registration, playerId);
        }
    }

    private void OnCustomHudClicked(
        CCSPlayerController player,
        CCSCustomHudLayout customLayout,
        string buttonId)
    {
        if (!player.IsValid || player.SteamID == 0 || string.IsNullOrWhiteSpace(buttonId))
        {
            return;
        }

        Registration? registration = null;
        lock (_gate)
        {
            registration = _registrations.Values.FirstOrDefault(candidate =>
                candidate.Entity is { IsValid: true }
                && candidate.Entity.Handle == customLayout.Handle);
        }

        if (registration is null
            || !registration.Definition.ButtonIds.Contains(buttonId, StringComparer.Ordinal)
            || registration.ClickHandler is null)
        {
            return;
        }

        PlayerId playerId;
        try
        {
            playerId = new PlayerId(player.SteamID);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        lock (_gate)
        {
            if (!registration.PlayerStates.TryGetValue(playerId, out var state) || !state.Visible)
            {
                return;
            }
        }

        _ = ObserveClickAsync(
            registration,
            new CustomHudClickContext(playerId, registration.Definition.Id, buttonId, _lifetime.Token));
    }

    private async Task ObserveClickAsync(Registration registration, CustomHudClickContext context)
    {
        try
        {
            await registration.ClickHandler!(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Custom HUD click handler failed for {HudId}, player {PlayerId}, button {ButtonId}.",
                registration.Definition.Id,
                context.PlayerId,
                context.ButtonId);
        }
    }

    private void ApplyPlayerState(Registration registration, PlayerId playerId)
    {
        if (!IsCurrent(registration) || !EnsureEntity(registration))
        {
            return;
        }

        var player = FindPlayer(playerId);
        if (player is null)
        {
            return;
        }

        HudPlayerState? state;
        lock (_gate)
        {
            registration.PlayerStates.TryGetValue(playerId, out state);
            if (state is not null)
            {
                state.LastSlot = player.Slot;
            }
        }

        if (state is null)
        {
            return;
        }

        foreach (var pair in state.Text)
        {
            registration.Entity!.SetDialogVariableStringForPlayer(
                player,
                pair.Key.PanelId,
                pair.Key.VariableName,
                pair.Value);
        }

        foreach (var pair in state.Classes)
        {
            registration.Entity!.SetHasClassForPlayer(
                player,
                pair.Key.PanelId,
                pair.Key.ClassName,
                pair.Value);
        }

        registration.Entity!.SetHasClassForPlayer(
            player,
            registration.Definition.RootPanelId,
            registration.Definition.VisibleClass,
            state.Visible);
        if (registration.Definition.CaptureInput)
        {
            registration.Entity!.SetInputCaptureEnabled(player, state.Visible);
        }
    }

    private void ApplyText(
        Registration registration,
        PlayerId playerId,
        string panelId,
        string variableName)
    {
        if (!IsCurrent(registration) || !EnsureEntity(registration))
        {
            return;
        }

        var player = FindPlayer(playerId);
        if (player is null)
        {
            return;
        }

        string? value;
        lock (_gate)
        {
            value = registration.PlayerStates.TryGetValue(playerId, out var state)
                && state.Text.TryGetValue((panelId, variableName), out var stored)
                    ? stored
                    : null;
        }

        if (value is not null)
        {
            registration.Entity!.SetDialogVariableStringForPlayer(player, panelId, variableName, value);
        }
    }

    private void ApplyClass(
        Registration registration,
        PlayerId playerId,
        string panelId,
        string className)
    {
        if (!IsCurrent(registration) || !EnsureEntity(registration))
        {
            return;
        }

        var player = FindPlayer(playerId);
        if (player is null)
        {
            return;
        }

        bool? enabled;
        lock (_gate)
        {
            enabled = registration.PlayerStates.TryGetValue(playerId, out var state)
                && state.Classes.TryGetValue((panelId, className), out var stored)
                    ? stored
                    : null;
        }

        if (enabled is not null)
        {
            registration.Entity!.SetHasClassForPlayer(player, panelId, className, enabled.Value);
        }
    }

    private bool EnsureEntity(Registration registration)
    {
        if (!IsCurrent(registration))
        {
            return false;
        }

        if (registration.Entity is { IsValid: true })
        {
            return true;
        }

        try
        {
            foreach (var existing in Utilities.FindAllEntitiesByDesignerName<CCSCustomHudLayout>("custom_hud_layout"))
            {
                if (existing.IsValid
                    && string.Equals(
                        existing.StrLayout,
                        registration.Definition.LayoutResource,
                        StringComparison.OrdinalIgnoreCase))
                {
                    existing.Remove();
                }
            }

            var entity = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
            if (entity is null || !entity.IsValid)
            {
                _logger.LogError(
                    "Could not create custom_hud_layout for {HudId} ({LayoutResource}).",
                    registration.Definition.Id,
                    registration.Definition.LayoutResource);
                return false;
            }

            entity.StrLayout = registration.Definition.LayoutResource;
            entity.DispatchSpawn();
            registration.Entity = entity;
            _logger.LogInformation(
                "Custom HUD {HudId} ready with layout {LayoutResource}.",
                registration.Definition.Id,
                registration.Definition.LayoutResource);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to create custom HUD {HudId} ({LayoutResource}).",
                registration.Definition.Id,
                registration.Definition.LayoutResource);
            return false;
        }
    }

    private void CleanupRegistration(Registration registration)
    {
        try
        {
            if (registration.Entity is not { IsValid: true } entity)
            {
                return;
            }

            foreach (var playerId in SnapshotVisiblePlayers(registration))
            {
                var player = FindPlayer(playerId);
                if (player is null)
                {
                    continue;
                }

                entity.SetHasClassForPlayer(
                    player,
                    registration.Definition.RootPanelId,
                    registration.Definition.VisibleClass,
                    false);
                if (registration.Definition.CaptureInput)
                {
                    entity.SetInputCaptureEnabled(player, false);
                }
            }

            entity.Remove();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to fully clean up custom HUD {HudId}.",
                registration.Definition.Id);
        }
        finally
        {
            registration.Entity = null;
            registration.PlayerStates.Clear();
        }
    }

    private void Unregister(Registration registration)
    {
        var removed = false;
        lock (_gate)
        {
            if (_registrations.TryGetValue(registration.Definition.Id, out var current)
                && ReferenceEquals(current, registration))
            {
                _registrations.Remove(registration.Definition.Id);
                _pendingCleanup.Add(registration);
                removed = true;
            }
        }

        if (!removed)
        {
            return;
        }

        Server.NextWorldUpdate(() =>
        {
            var shouldCleanup = false;
            lock (_gate)
            {
                shouldCleanup = _pendingCleanup.Remove(registration);
            }

            if (shouldCleanup)
            {
                CleanupRegistration(registration);
            }
        });
    }

    private bool IsCurrent(Registration registration)
    {
        lock (_gate)
        {
            return !_disposed
                && _registrations.TryGetValue(registration.Definition.Id, out var current)
                && ReferenceEquals(current, registration);
        }
    }

    private PlayerId[] SnapshotVisiblePlayers(Registration registration)
    {
        lock (_gate)
        {
            return registration.PlayerStates
                .Where(pair => pair.Value.Visible)
                .Select(pair => pair.Key)
                .ToArray();
        }
    }

    private static CCSPlayerController? FindPlayer(PlayerId playerId)
        => Utilities.GetPlayers().FirstOrDefault(player =>
            player.IsValid
            && !player.IsBot
            && player.SteamID == playerId.SteamId64);

    private static string ValidateToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Custom HUD identifiers cannot be empty.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ArgumentException(
                "Custom HUD identifiers may contain only ASCII letters, digits, '_' and '-'.",
                parameterName);
        }

        return normalized;
    }

    private sealed class Registration(
        ModuleId owner,
        CustomHudDefinition definition,
        CustomHudClickHandler? clickHandler)
    {
        public ModuleId Owner { get; } = owner;
        public CustomHudDefinition Definition { get; } = definition;
        public CustomHudClickHandler? ClickHandler { get; } = clickHandler;
        public CCSCustomHudLayout? Entity { get; set; }
        public Dictionary<PlayerId, HudPlayerState> PlayerStates { get; } = [];

        public HudPlayerState GetOrCreateState(PlayerId playerId)
        {
            if (!PlayerStates.TryGetValue(playerId, out var state))
            {
                state = new HudPlayerState();
                PlayerStates.Add(playerId, state);
            }

            return state;
        }
    }

    private sealed class HudPlayerState
    {
        public bool Visible { get; set; }
        public int? LastSlot { get; set; }
        public Dictionary<(string PanelId, string VariableName), string> Text { get; } = [];
        public Dictionary<(string PanelId, string ClassName), bool> Classes { get; } = [];
    }

    private sealed class RegistrationHandle(
        CounterStrikeCustomHudService owner,
        Registration registration) : IDisposable
    {
        private CounterStrikeCustomHudService? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.Unregister(registration);
    }
}
