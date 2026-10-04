using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Modules.Admin;
using AnoCore.Modules.AnoVeto;
using AnoCore.Plugin.Administration;
using AnoCore.Plugin.Commands;
using AnoCore.Plugin.Maps;
using AnoCore.Plugin.Menus;
using AnoCore.Plugin.Moderation;
using AnoCore.Plugin.Players;
using AnoCore.Runtime.Composition;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Players;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;

namespace AnoCore.Plugin;

[MinimumApiVersion(374)]
public sealed class AnoCorePlugin : BasePlugin
{
    private readonly object _startupGate = new();
    private AnoEventBus? _eventBus;
    private PlayerRegistry? _players;
    private bool _lifecycleHooksRegistered;
    private CancellationTokenSource? _startup;
    private RuntimeServices? _pendingRuntime;
    private AnoVetoModuleRuntime? _pendingAnoVeto;
    private RuntimeServices? _runtime;
    private AnoVetoModuleRuntime? _anoVeto;
    private ModerationCommandController? _adminCommands;
    private ExtendedPlayerStateCommandController? _extendedAdminCommands;
    private ExtendedPlayerStateService? _extendedPlayerState;
    private ExtendedPositionCommandController? _extendedPositionCommands;
    private ExtendedPositionService? _extendedPositions;
    private ExtendedInventoryTeamCommandController? _extendedInventoryTeamCommands;
    private ModerationCommunicationRuntime? _communicationModeration;
    private CounterStrikeChatModerationAdapter? _chatModeration;
    private ModerationVoiceCoordinator? _voiceModeration;
    private CounterStrikeCommandBridge? _commands;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _anoVetoExpiryTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _voiceModerationTimer;
    private string _runtimeStatus = "not started";

    public RuntimeServices? Runtime => _runtime;

    public CounterStrikeMenuPresenter? MenuPresenter { get; private set; }

    public override string ModuleName => "AnoCore";

    public override string ModuleDescription => "Modular CS2 server framework for Ano modules.";

    public override string ModuleAuthor => "AnoMeme contributors";

    public override string ModuleVersion => "0.1.0-dev";

    public override void Load(bool hotReload)
    {
        _eventBus = new AnoEventBus();
        _players = new PlayerRegistry(_eventBus);

        RegisterLifecycleHooks();

        AddCommand("css_anostatus", "Show AnoCore runtime status", OnStatus);
        BootstrapConnectedPlayers();
        _startup = new CancellationTokenSource();
        _runtimeStatus = "starting";
        _ = InitializeRuntimeAsync(_eventBus, _players, _startup.Token);
    }

    public override void Unload(bool hotReload)
    {
        lock (_startupGate)
        {
            _startup?.Cancel();
            _startup?.Dispose();
            _startup = null;

            _anoVetoExpiryTimer?.Kill();
            _anoVetoExpiryTimer = null;
            _voiceModerationTimer?.Kill();
            _voiceModerationTimer = null;

            _pendingAnoVeto?.Dispose();
            _pendingAnoVeto = null;
            _pendingRuntime?.Dispose();
            _pendingRuntime = null;

            _voiceModeration?.Dispose();
            _voiceModeration = null;
            _chatModeration?.Dispose();
            _chatModeration = null;
            _communicationModeration?.Dispose();
            _communicationModeration = null;
            _anoVeto?.Dispose();
            _anoVeto = null;
            _commands?.Dispose();
            _commands = null;
            _extendedInventoryTeamCommands?.Dispose();
            _extendedInventoryTeamCommands = null;
            _extendedPositionCommands?.Dispose();
            _extendedPositionCommands = null;
            var extendedPositions = _extendedPositions;
            _extendedPositions = null;
            if (extendedPositions is not null)
            {
                Observe(extendedPositions.ForgetAllAsync().AsTask(), "extended_position_unload");
            }

            _extendedAdminCommands?.Dispose();
            _extendedAdminCommands = null;
            var extendedPlayerState = _extendedPlayerState;
            _extendedPlayerState = null;
            if (extendedPlayerState is not null)
            {
                Observe(
                    ReleaseAndDisposeExtendedPlayerStateAsync(extendedPlayerState),
                    "extended_admin_unload");
            }

            _adminCommands?.Dispose();
            _adminCommands = null;
            _runtime?.Dispose();
            _runtime = null;
            MenuPresenter = null;
            _runtimeStatus = "stopped";
        }

        RemoveCommand("css_anostatus", OnStatus);
        DeregisterLifecycleHooks();
        _players = null;
        _eventBus = null;
    }

    private void OnStatus(CCSPlayerController? player, CommandInfo command)
    {
        var optionalModules = (_runtime?.Modules.Modules.Count ?? 0) + (_anoVeto is null ? 0 : 1);
        command.ReplyToCommand(
            $"[ANO] AnoCore {ModuleVersion}; tracked humans: {_players?.OnlinePlayers.Count ?? 0}; "
            + $"services: {_runtimeStatus}; optional gameplay modules: {optionalModules}.");
    }

    private async Task InitializeRuntimeAsync(
        AnoEventBus events,
        PlayerRegistry players,
        CancellationToken cancellationToken)
    {
        RuntimeServices? created = null;
        AnoVetoModuleRuntime? createdAnoVeto = null;
        try
        {
            var configuration = new JsonConfigStore(Path.Combine(ModuleDirectory, "config"));
            var settings = await configuration.LoadAsync(
                "core",
                () => new RuntimeConfiguration(),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var connectionString = Environment.GetEnvironmentVariable("ANOCORE_MYSQL");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString = settings.ConnectionString;
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                lock (_startupGate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _runtimeStatus = "not configured";
                    Logger.LogWarning("AnoCore requires ANOCORE_MYSQL or config/core.json ConnectionString.");
                }

                return;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            created = await RuntimeServices.CreateAsync(
                new MySqlDatabase(connectionString),
                configuration,
                events,
                players,
                timeout.Token).ConfigureAwait(false);

            try
            {
                var votes = created.GetService(typeof(IVoteService)) as IVoteService
                    ?? throw new InvalidOperationException("AnoCore runtime did not provide the shared vote service.");
                createdAnoVeto = await AnoVetoModuleRuntime.CreateAsync(
                    configuration,
                    created.Commands,
                    created.Menus,
                    created.Players,
                    votes,
                    new CounterStrikeMapChanger(),
                    cancellationToken: timeout.Token,
                    reloads: created.ConfigReloads).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                createdAnoVeto?.Dispose();
                createdAnoVeto = null;
                Logger.LogError(
                    exception,
                    "AnoVeto configuration/composition failed; AnoCore will continue without AnoVeto.");
            }

            lock (_startupGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _pendingRuntime = created;
                _pendingAnoVeto = createdAnoVeto;
                created = null;
                createdAnoVeto = null;
                Server.NextWorldUpdate(() => ActivateRuntime(cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            createdAnoVeto?.Dispose();
            created?.Dispose();
        }
        catch (Exception exception)
        {
            createdAnoVeto?.Dispose();
            created?.Dispose();
            lock (_startupGate)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _pendingAnoVeto?.Dispose();
                    _pendingAnoVeto = null;
                    _pendingRuntime?.Dispose();
                    _pendingRuntime = null;
                    _runtimeStatus = "startup failed";
                    Logger.LogError(
                        "AnoCore startup failed ({ErrorType}); check configuration and database availability.",
                        exception.GetType().Name);
                }
            }
        }
    }

    private void ActivateRuntime(CancellationToken cancellationToken)
    {
        lock (_startupGate)
        {
            if (cancellationToken.IsCancellationRequested || _pendingRuntime is null)
            {
                return;
            }

            var runtime = _pendingRuntime;
            var anoVeto = _pendingAnoVeto;
            _pendingRuntime = null;
            _pendingAnoVeto = null;
            ModerationCommandController? adminCommands = null;
            ExtendedPlayerStateService? extendedPlayerState = null;
            ExtendedPlayerStateCommandController? extendedAdminCommands = null;
            ExtendedPositionService? extendedPositions = null;
            ExtendedPositionCommandController? extendedPositionCommands = null;
            ExtendedInventoryTeamCommandController? extendedInventoryTeamCommands = null;
            ModerationCommunicationRuntime? communicationModeration = null;
            CounterStrikeChatModerationAdapter? chatModeration = null;
            ModerationVoiceCoordinator? voiceModeration = null;
            var presenter = new CounterStrikeMenuPresenter(this, runtime.Menus, Logger);
            var bridge = new CounterStrikeCommandBridge(
                this,
                runtime.Commands,
                Logger,
                (commandName, player) =>
                {
                    if (string.Equals(commandName, "anoveto", StringComparison.Ordinal))
                    {
                        presenter.Reconcile();
                        presenter.Open(player);
                    }
                });
            CounterStrikeSharp.API.Modules.Timers.Timer? expiryTimer = null;
            CounterStrikeSharp.API.Modules.Timers.Timer? voiceTimer = null;

            try
            {
                var targetGateway = new ModerationTargetGateway(
                    runtime.Players,
                    runtime.TargetResolver,
                    runtime.TargetAuthorization,
                    runtime.Authorization);
                adminCommands = new ModerationCommandController(
                    runtime.Commands,
                    new ModerationCommandExecutor(targetGateway, runtime.Moderation));
                extendedPlayerState = new ExtendedPlayerStateService(
                    new CounterStrikeExtendedPlayerStateTransport(runtime.Players));
                extendedAdminCommands = new ExtendedPlayerStateCommandController(
                    runtime.Commands,
                    new ExtendedPlayerStateCommandExecutor(
                        targetGateway,
                        extendedPlayerState));
                extendedPositions = new ExtendedPositionService(
                    new CounterStrikeExtendedPositionTransport(runtime.Players));
                extendedPositionCommands = new ExtendedPositionCommandController(
                    runtime.Commands,
                    new ExtendedPositionCommandExecutor(
                        targetGateway,
                        runtime.TargetResolver,
                        extendedPositions));
                extendedInventoryTeamCommands = new ExtendedInventoryTeamCommandController(
                    runtime.Commands,
                    new ExtendedInventoryTeamCommandExecutor(
                        targetGateway,
                        runtime.Players,
                        runtime.Authorization,
                        new CounterStrikeExtendedInventoryTeamTransport(runtime.Players)));

                var events = _eventBus
                    ?? throw new InvalidOperationException("AnoCore event bus is unavailable during activation.");
                communicationModeration = new ModerationCommunicationRuntime(
                    events,
                    runtime.Moderation,
                    runtime.Moderation);
                chatModeration = new CounterStrikeChatModerationAdapter(
                    this,
                    communicationModeration.ChatGate);
                voiceModeration = new ModerationVoiceCoordinator(
                    runtime.Players,
                    communicationModeration.VoiceGate,
                    new CounterStrikeVoiceModerationTransport(runtime.Players));
                var activeVoiceModeration = voiceModeration;
                activeVoiceModeration.Reconcile();
                Observe(
                    communicationModeration
                        .WarmExistingAsync(runtime.Players.OnlinePlayers.ToArray())
                        .AsTask(),
                    "moderation_communication_bootstrap");
                voiceTimer = AddTimer(
                    0.25f,
                    () => ReconcileVoiceModeration(activeVoiceModeration),
                    TimerFlags.REPEAT);

                foreach (var descriptor in runtime.Commands.GetCommands())
                {
                    bridge.Bind(descriptor);
                }

                if (anoVeto is not null)
                {
                    expiryTimer = AddTimer(
                        1.0f,
                        () => _ = ExpireAnoVetoAsync(anoVeto, presenter),
                        TimerFlags.REPEAT);
                }

                MenuPresenter = presenter;
                _adminCommands = adminCommands;
                _extendedPlayerState = extendedPlayerState;
                _extendedAdminCommands = extendedAdminCommands;
                _extendedPositions = extendedPositions;
                _extendedPositionCommands = extendedPositionCommands;
                _extendedInventoryTeamCommands = extendedInventoryTeamCommands;
                _communicationModeration = communicationModeration;
                _chatModeration = chatModeration;
                _voiceModeration = voiceModeration;
                _commands = bridge;
                _runtime = runtime;
                _anoVeto = anoVeto;
                _anoVetoExpiryTimer = expiryTimer;
                _voiceModerationTimer = voiceTimer;
                _runtimeStatus = "ready";
                Logger.LogInformation(
                    "AnoCore shared services ready; database/authorization initialized; AnoVeto {AnoVetoState}.",
                    anoVeto is null ? "disabled" : "active");
            }
            catch (Exception exception)
            {
                expiryTimer?.Kill();
                voiceTimer?.Kill();
                voiceModeration?.Dispose();
                chatModeration?.Dispose();
                communicationModeration?.Dispose();
                anoVeto?.Dispose();
                bridge.Dispose();
                extendedInventoryTeamCommands?.Dispose();
                extendedPositionCommands?.Dispose();
                extendedAdminCommands?.Dispose();
                extendedPlayerState?.Dispose();
                adminCommands?.Dispose();
                runtime.Dispose();
                MenuPresenter = null;
                _runtimeStatus = "activation failed";
                Logger.LogError(exception, "AnoCore command/menu/module activation failed.");
            }
        }
    }

    private void ReconcileVoiceModeration(ModerationVoiceCoordinator voiceModeration)
    {
        try
        {
            if (ReferenceEquals(_voiceModeration, voiceModeration))
            {
                voiceModeration.Reconcile();
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(
                exception,
                "AnoCore runtime operation {Operation} failed.",
                "moderation_voice_reconcile");
        }
    }

    private async Task ExpireAnoVetoAsync(AnoVetoModuleRuntime anoVeto, CounterStrikeMenuPresenter presenter)
    {
        try
        {
            var result = await anoVeto.ExpireAsync().ConfigureAwait(false);
            if (result is null)
            {
                return;
            }

            Server.NextWorldUpdate(() =>
            {
                if (ReferenceEquals(_anoVeto, anoVeto) && ReferenceEquals(MenuPresenter, presenter))
                {
                    presenter.Reconcile();
                }
            });
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "AnoCore runtime operation {Operation} failed.", "anoveto_expire");
        }
    }

    public sealed class RuntimeConfiguration
    {
        public string ConnectionString { get; set; } = string.Empty;
    }

    private void RegisterLifecycleHooks()
    {
        if (_lifecycleHooksRegistered)
        {
            return;
        }

        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeathPosition, HookMode.Pre);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
        _lifecycleHooksRegistered = true;
    }

    private void DeregisterLifecycleHooks()
    {
        if (!_lifecycleHooksRegistered)
        {
            return;
        }

        DeregisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        DeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        DeregisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
        DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        DeregisterEventHandler<EventPlayerDeath>(OnPlayerDeathPosition, HookMode.Pre);
        DeregisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
        _lifecycleHooksRegistered = false;
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo _)
    {
        TrackConnection(@event.Userid, "player_connect_full");
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo _)
    {
        if (_players is null || @event.Xuid == 0)
        {
            return HookResult.Continue;
        }

        PlayerId id;
        try
        {
            id = new PlayerId(@event.Xuid);
        }
        catch (ArgumentOutOfRangeException)
        {
            return HookResult.Continue;
        }

        if (_players.TryGet(id, out var current) && current is not null)
        {
            var registry = _players;
            var state = _extendedPlayerState;
            var positions = _extendedPositions;
            Observe(
                ReleaseExtendedStateThenDisconnectAsync(
                    registry,
                    state,
                    positions,
                    current,
                    DateTimeOffset.UtcNow),
                "player_disconnect");
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo _)
    {
        RefreshNextFrame(@event.Userid, "player_team");
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo _)
    {
        RefreshNextFrame(@event.Userid, "player_spawn");
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeathPosition(EventPlayerDeath @event, GameEventInfo _)
    {
        var registry = _players;
        var positions = _extendedPositions;
        var controller = @event.Userid;
        if (registry is null
            || positions is null
            || controller is null
            || !controller.IsValid
            || controller.SteamID == 0)
        {
            return HookResult.Continue;
        }

        PlayerId id;
        try
        {
            id = new PlayerId(controller.SteamID);
        }
        catch (ArgumentOutOfRangeException)
        {
            return HookResult.Continue;
        }

        var origin = controller.PlayerPawn.Value?.AbsOrigin;
        if (origin is not null
            && registry.TryGet(id, out var current)
            && current is not null
            && current.IsConnected)
        {
            try
            {
                positions.RecordDeathPosition(
                    current,
                    new PlayerWorldPosition(origin.X, origin.Y, origin.Z));
            }
            catch (ArgumentOutOfRangeException)
            {
                Logger.LogWarning(
                    "Skipped an out-of-range death position for player {PlayerId}.",
                    id);
            }
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo _)
    {
        ReleaseExtendedStateForController(@event.Userid, "extended_admin_player_death");
        RefreshNextFrame(@event.Userid, "player_death");
        return HookResult.Continue;
    }

    private void OnMapEnd()
    {
        var state = _extendedPlayerState;
        if (state is not null)
        {
            Observe(state.ForgetAllAsync().AsTask(), "extended_admin_map_end");
        }

        var positions = _extendedPositions;
        if (positions is not null)
        {
            Observe(positions.ForgetAllAsync().AsTask(), "extended_position_map_end");
        }
    }

    private void BootstrapConnectedPlayers()
    {
        foreach (var controller in Utilities.GetPlayers())
        {
            TrackConnection(controller, "load_bootstrap");
        }
    }

    private void TrackConnection(CCSPlayerController? controller, string operation)
    {
        if (_players is null
            || !CounterStrikePlayerMapper.TryCreateConnection(
                controller,
                DateTimeOffset.UtcNow,
                out var connection)
            || connection is null)
        {
            return;
        }

        Observe(_players.ConnectAsync(connection).AsTask(), operation);
    }

    private void RefreshNextFrame(CCSPlayerController? controller, string operation)
    {
        if (controller is null)
        {
            return;
        }

        var registry = _players;
        Server.NextFrame(() =>
        {
            if (registry is not null && ReferenceEquals(registry, _players))
            {
                RefreshPlayer(controller, operation);
            }
        });
    }

    private void RefreshPlayer(CCSPlayerController? controller, string operation)
    {
        if (_players is null
            || controller is null
            || !controller.IsValid
            || controller.SteamID == 0)
        {
            return;
        }

        PlayerId id;
        try
        {
            id = new PlayerId(controller.SteamID);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        if (!_players.TryGet(id, out var current)
            || current is null
            || !CounterStrikePlayerMapper.TryCreateUpdate(
                controller,
                current.SessionId,
                DateTimeOffset.UtcNow,
                out var update)
            || update is null)
        {
            return;
        }

        Observe(_players.UpdateAsync(update).AsTask(), operation);
    }

    private void ReleaseExtendedStateForController(
        CCSPlayerController? controller,
        string operation)
    {
        var registry = _players;
        var state = _extendedPlayerState;
        if (registry is null
            || state is null
            || controller is null
            || !controller.IsValid
            || controller.SteamID == 0)
        {
            return;
        }

        PlayerId id;
        try
        {
            id = new PlayerId(controller.SteamID);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        if (registry.TryGet(id, out var current) && current is not null)
        {
            Observe(state.ReleaseSessionAsync(current).AsTask(), operation);
        }
    }

    private static async Task ReleaseExtendedStateThenDisconnectAsync(
        PlayerRegistry registry,
        ExtendedPlayerStateService? state,
        ExtendedPositionService? positions,
        PlayerSnapshot current,
        DateTimeOffset disconnectedAtUtc)
    {
        if (state is not null)
        {
            await state.ForgetSessionAsync(current.SessionId).ConfigureAwait(false);
        }

        if (positions is not null)
        {
            await positions.ForgetSessionAsync(current.SessionId).ConfigureAwait(false);
        }

        await registry.DisconnectAsync(
            current.Id,
            current.SessionId,
            disconnectedAtUtc).ConfigureAwait(false);
    }

    private static async Task ReleaseAndDisposeExtendedPlayerStateAsync(
        ExtendedPlayerStateService state)
    {
        try
        {
            await state.ReleaseAllAsync().ConfigureAwait(false);
        }
        finally
        {
            state.Dispose();
        }
    }

    private void Observe(Task operation, string context)
        => _ = ObserveAsync(operation, context);

    private async Task ObserveAsync(Task operation, string context)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "AnoCore runtime operation {Operation} failed.", context);
        }
    }
}
