using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Placeholders;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Abstractions.Voting;
using AnoCore.Modules.Admin;
using AnoCore.Modules.AnoVeto;
using AnoCore.Modules.Stats;
using AnoCore.Plugin.Administration;
using AnoCore.Plugin.Commands;
using AnoCore.Plugin.Hud;
using AnoCore.Plugin.Maps;
using AnoCore.Plugin.Menus;
using AnoCore.Plugin.Moderation;
using AnoCore.Plugin.Players;
using AnoCore.Runtime.Composition;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Players;
using AnoCore.Runtime.Settings;
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
    private PlaytimeModule? _pendingPlaytime;
    private RankModule? _pendingRank;
    private ChatMessageFormatter? _pendingChatFormatter;
    private SelectableChatTagModule? _pendingChatTags;
    private ProtectedServerControlPolicy? _pendingProtectedServerControlPolicy;
    private RuntimeServices? _runtime;
    private AnoVetoModuleRuntime? _anoVeto;
    private PlaytimeModule? _playtime;
    private RankModule? _rank;
    private ChatMessageFormatter? _chatFormatter;
    private SelectableChatTagModule? _chatTags;
    private ChatFormatSnapshotLifecycle? _chatFormatSnapshots;
    private CombatModule? _combat;
    private string _combatServerInstance = string.Empty;
    private ModerationCommandController? _adminCommands;
    private RankAdjustmentCommandController? _rankAdminCommands;
    private RankAdjustmentNotificationService? _rankAdminNotifications;
    private KickCommandController? _kickCommands;
    private ConnectBanEnforcement? _connectBan;
    private WarningCommandController? _warningCommands;
    private ExtendedPlayerStateCommandController? _extendedAdminCommands;
    private ExtendedPlayerStateService? _extendedPlayerState;
    private ExtendedPositionCommandController? _extendedPositionCommands;
    private ExtendedPositionService? _extendedPositions;
    private ExtendedInventoryTeamCommandController? _extendedInventoryTeamCommands;
    private ProtectedServerControlCommandController? _protectedServerControlCommands;
    private ModerationCommunicationRuntime? _communicationModeration;
    private CounterStrikeChatModerationAdapter? _chatModeration;
    private ModerationVoiceCoordinator? _voiceModeration;
    private CounterStrikeCommandBridge? _commands;
    private CounterStrikeCustomHudService? _customHud;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _anoVetoExpiryTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _voiceModerationTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _playtimeTimer;
    private string _runtimeStatus = "not started";

    public RuntimeServices? Runtime => _runtime;

    public CounterStrikeMenuPresenter? MenuPresenter { get; private set; }

    public ChatMessageFormatter? ChatFormatter => _chatFormatter;

    public ChatFormatSnapshotLifecycle? ChatFormatSnapshots => _chatFormatSnapshots;

    public override string ModuleName => "AnoCore";

    public override string ModuleDescription => "Modular CS2 server framework for Ano modules.";

    public override string ModuleAuthor => "AnoMeme contributors";

    public override string ModuleVersion => "0.1.0-dev";

    public override void Load(bool hotReload)
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        _combatServerInstance = $"{Environment.ProcessId}-{process.StartTime.ToUniversalTime().Ticks}";
        _eventBus = new AnoEventBus();
        _players = new PlayerRegistry(_eventBus);
        _customHud = new CounterStrikeCustomHudService(this, Logger);
        _customHud.Start();

        RegisterLifecycleHooks();

        AddCommand("css_anostatus", "Show AnoCore runtime status", OnStatus);
        BootstrapConnectedPlayers();
        _startup = new CancellationTokenSource();
        _runtimeStatus = "starting";
        _ = InitializeRuntimeAsync(_eventBus, _players, _customHud, _startup.Token);
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
            _playtimeTimer?.Kill();
            _playtimeTimer = null;
            if (_playtime is not null)
                Observe(_playtime.CheckpointOnlineAsync(DateTimeOffset.UtcNow).AsTask(), "playtime_unload");
            _playtime?.Dispose();
            _playtime = null;
            _chatFormatSnapshots?.Dispose();
            _chatFormatSnapshots = null;
            _chatTags?.Dispose();
            _chatTags = null;
            _rank?.Dispose();
            _rank = null;
            _chatFormatter = null;
            _combat?.Dispose();
            _combat = null;
            _pendingPlaytime?.Dispose();
            _pendingPlaytime = null;
            _pendingRank?.Dispose();
            _pendingRank = null;
            _pendingChatFormatter = null;
            _pendingChatTags?.Dispose();
            _pendingChatTags = null;

            _pendingAnoVeto?.Dispose();
            _pendingAnoVeto = null;
            _pendingProtectedServerControlPolicy = null;
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
            _rankAdminCommands?.Dispose();
            _rankAdminCommands = null;
            _rankAdminNotifications?.Dispose();
            _rankAdminNotifications = null;
            _warningCommands?.Dispose();
            _warningCommands = null;
            _protectedServerControlCommands?.Dispose();
            _protectedServerControlCommands = null;
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
            _kickCommands?.Dispose();
            _kickCommands = null;
            _connectBan?.Dispose();
            _connectBan = null;
            _runtime?.Dispose();
            _runtime = null;
            MenuPresenter = null;
            _customHud?.Dispose();
            _customHud = null;
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
        ICustomHudService hud,
        CancellationToken cancellationToken)
    {
        RuntimeServices? created = null;
        AnoVetoModuleRuntime? createdAnoVeto = null;
        PlaytimeModule? createdPlaytime = null;
        RankModule? createdRank = null;
        ChatMessageFormatter? createdChatFormatter = null;
        SelectableChatTagModule? createdChatTags = null;
        try
        {
            var configuration = new JsonConfigStore(Path.Combine(ModuleDirectory, "config"));
            var settings = await configuration.LoadAsync(
                "core",
                () => new RuntimeConfiguration(),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var protectedServerControlPolicy = BuildProtectedServerControlPolicy(
                settings.ProtectedServerControls);
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
                createdPlaytime = await PlaytimeModule.CreateAsync(
                    events, players, created.Playtime, created.Commands,
                    cancellationToken: timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                createdPlaytime?.Dispose();
                createdPlaytime = null;
                Logger.LogError(exception,
                    "Stats composition failed; AnoCore will continue without playtime tracking.");
            }

            try
            {
                var placeholders = created.GetService(typeof(IPlaceholderRegistry))
                    as IPlaceholderRegistry
                    ?? throw new InvalidOperationException(
                        "AnoCore runtime did not provide the shared placeholder registry.");
                createdRank = await RankModule.CreateAsync(
                    configuration, created.Commands, players, created.Combat,
                    created.Menus, placeholders, created.ToggleCatalog, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                createdRank?.Dispose();
                createdRank = null;
                Logger.LogError(exception,
                    "Rank composition failed; AnoCore will continue without ranks.");
            }

            try
            {
                var placeholders = created.GetService(typeof(IPlaceholderRegistry))
                    as IPlaceholderRegistry
                    ?? throw new InvalidOperationException(
                        "AnoCore runtime did not provide the shared placeholder registry.");
                createdChatFormatter = await ChatMessageFormatter.CreateAsync(
                    configuration, placeholders, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                createdChatFormatter = null;
                Logger.LogError(exception,
                    "Chat formatting composition failed; AnoCore will continue without chat formatting.");
            }

            try
            {
                var placeholders = created.GetService(typeof(IPlaceholderRegistry))
                    as IPlaceholderRegistry
                    ?? throw new InvalidOperationException(
                        "AnoCore runtime did not provide the shared placeholder registry.");
                createdChatTags = await SelectableChatTagModule.CreateAsync(
                    configuration, created.Commands, placeholders, players, created.Settings,
                    created.Authorization, created.Authorization,
                    (player, token) => _chatFormatSnapshots is { } snapshots
                        ? snapshots.RefreshTagPolicyAsync(player, token)
                        : ValueTask.CompletedTask,
                    created.Menus,
                    events,
                    exception => Logger.LogError(
                        exception, "Chat tag snapshot refresh failed."),
                    timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                createdChatTags?.Dispose();
                createdChatTags = null;
                Logger.LogError(exception,
                    "Chat tag composition failed; AnoCore will continue without selectable tags.");
            }

            try
            {
                var votes = created.GetService(typeof(IVoteService)) as IVoteService
                    ?? throw new InvalidOperationException("AnoCore runtime did not provide the shared vote service.");
                createdAnoVeto = await AnoVetoModuleRuntime.CreateAsync(
                    configuration,
                    created.Commands,
                    hud,
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
                _pendingPlaytime = createdPlaytime;
                _pendingRank = createdRank;
                _pendingChatFormatter = createdChatFormatter;
                _pendingChatTags = createdChatTags;
                _pendingProtectedServerControlPolicy = protectedServerControlPolicy;
                created = null;
                createdAnoVeto = null;
                createdPlaytime = null;
                createdRank = null;
                createdChatFormatter = null;
                createdChatTags = null;
                Server.NextWorldUpdate(() => ActivateRuntime(cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            createdAnoVeto?.Dispose();
            createdPlaytime?.Dispose();
            createdRank?.Dispose();
            createdChatTags?.Dispose();
            created?.Dispose();
        }
        catch (Exception exception)
        {
            createdAnoVeto?.Dispose();
            createdPlaytime?.Dispose();
            createdRank?.Dispose();
            createdChatTags?.Dispose();
            created?.Dispose();
            lock (_startupGate)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _pendingPlaytime?.Dispose();
                    _pendingPlaytime = null;
                    _pendingRank?.Dispose();
                    _pendingRank = null;
                    _pendingChatFormatter = null;
                    _pendingChatTags?.Dispose();
                    _pendingChatTags = null;
                    _pendingAnoVeto?.Dispose();
                    _pendingAnoVeto = null;
                    _pendingProtectedServerControlPolicy = null;
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
            var playtime = _pendingPlaytime;
            var rank = _pendingRank;
            var chatFormatter = _pendingChatFormatter;
            var chatTags = _pendingChatTags;
            _pendingRuntime = null;
            _pendingAnoVeto = null;
            _pendingPlaytime = null;
            _pendingRank = null;
            _pendingChatFormatter = null;
            _pendingChatTags = null;
            var protectedServerControlPolicy = _pendingProtectedServerControlPolicy
                ?? ProtectedServerControlPolicy.Create(
                    new ProtectedServerControlConfiguration());
            _pendingProtectedServerControlPolicy = null;
            ModerationCommandController? adminCommands = null;
            RankAdjustmentCommandController? rankAdminCommands = null;
            RankAdjustmentNotificationService? rankAdminNotifications = null;
            RankTransitionMonitor? transitionMonitor = null;
            CombatModule? combat = null;
            KickCommandController? kickCommands = null;
            ConnectBanEnforcement? connectBan = null;
            WarningCommandController? warningCommands = null;
            ExtendedPlayerStateService? extendedPlayerState = null;
            ExtendedPlayerStateCommandController? extendedAdminCommands = null;
            ExtendedPositionService? extendedPositions = null;
            ExtendedPositionCommandController? extendedPositionCommands = null;
            ExtendedInventoryTeamCommandController? extendedInventoryTeamCommands = null;
            ProtectedServerControlCommandController? protectedServerControlCommands = null;
            ModerationCommunicationRuntime? communicationModeration = null;
            CounterStrikeChatModerationAdapter? chatModeration = null;
            ChatFormatSnapshotLifecycle? chatFormatSnapshots = null;
            ModerationVoiceCoordinator? voiceModeration = null;
            var presenter = new CounterStrikeMenuPresenter(this, runtime.Menus, Logger);
            var bridge = new CounterStrikeCommandBridge(
                this,
                runtime.Commands,
                Logger,
                (commandName, player) =>
                {
                    if (string.Equals(commandName, RankModule.MenuCommandName,
                            StringComparison.Ordinal)
                        || string.Equals(commandName, SelectableChatTagModule.MenuCommandName,
                            StringComparison.Ordinal)
                        || string.Equals(commandName,
                            PlayerToggleCommandModule.MenuCommandName,
                            StringComparison.Ordinal))
                    {
                        presenter.Reconcile();
                        presenter.Open(player);
                    }
                });
            CounterStrikeSharp.API.Modules.Timers.Timer? expiryTimer = null;
            CounterStrikeSharp.API.Modules.Timers.Timer? voiceTimer = null;
            CounterStrikeSharp.API.Modules.Timers.Timer? playtimeTimer = null;

            try
            {
                var rankScoreChanges = new ChatFormatRankScoreChangeSink(
                    runtime.Players,
                    () => chatFormatSnapshots);
                var targetGateway = new ModerationTargetGateway(
                    runtime.Players,
                    runtime.TargetResolver,
                    runtime.TargetAuthorization,
                    runtime.Authorization);
                adminCommands = new ModerationCommandController(
                    runtime.Commands,
                    new ModerationCommandExecutor(targetGateway, runtime.Moderation));
                rankAdminNotifications = rank is null
                    ? null
                    : new RankAdjustmentNotificationService(
                        rank.Configuration,
                        runtime.RankAdjustmentAdministration,
                        runtime.Combat,
                        new RankNotificationPreferenceSink(
                            runtime.Settings,
                            new CounterStrikeRankTransitionNotifier(runtime.Players),
                            exception => Logger.LogError(
                                exception, "Rank notification preference read failed.")),
                        exception => Logger.LogError(
                            exception, "Rank adjustment notification failed."),
                        rankScoreChanges);
                IRankAdjustmentAdministrationService rankAdministration =
                    (IRankAdjustmentAdministrationService?)rankAdminNotifications
                    ?? runtime.RankAdjustmentAdministration;
                rankAdminCommands = new RankAdjustmentCommandController(
                    runtime.Commands,
                    new RankAdjustmentCommandExecutor(
                        targetGateway, rankAdministration));
                var disconnect = new CounterStrikePlayerDisconnectAction(runtime.Players, cancellationToken);
                kickCommands = new KickCommandController(
                    runtime.Commands,
                    new KickCommandExecutor(
                        targetGateway,
                        runtime.AdminAudit,
                        disconnect,
                        new CounterStrikeKickAnnouncement(cancellationToken)));
                warningCommands = new WarningCommandController(runtime.Commands,
                    new WarningCommandExecutor(targetGateway, runtime.Warnings, runtime.AdminAudit,
                        new CounterStrikeWarningNotifier(runtime.Players, Logger,
                            () => ReferenceEquals(_runtime, runtime))));
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
                protectedServerControlCommands = new ProtectedServerControlCommandController(
                    runtime.Commands,
                    new ProtectedServerControlExecutor(
                        protectedServerControlPolicy,
                        runtime.Authorization,
                        runtime.AdminAudit,
                        new CounterStrikeProtectedServerControlTransport(runtime.Players)));

                transitionMonitor = rank is null
                    ? null
                    : new RankTransitionMonitor(
                        rank.Configuration,
                        runtime.Combat,
                        new RankNotificationPreferenceSink(
                            runtime.Settings,
                            new CounterStrikeRankTransitionNotifier(runtime.Players),
                            exception => Logger.LogError(
                                exception, "Rank notification preference read failed.")),
                        rankScoreChanges);
                combat = new CombatModule(
                    runtime.Commands, runtime.Players, runtime.Combat, transitionMonitor);
                transitionMonitor = null;
                var events = _eventBus
                    ?? throw new InvalidOperationException("AnoCore event bus is unavailable during activation.");
                if (chatFormatter is not null)
                {
                    chatFormatSnapshots = new ChatFormatSnapshotLifecycle(
                        events,
                        chatFormatter,
                        exception => Logger.LogError(
                            exception, "Chat format snapshot warm failed."));
                    Observe(
                        chatFormatSnapshots.WarmExistingAsync(
                            runtime.Players.OnlinePlayers.ToArray()).AsTask(),
                        "chat_format_snapshot_bootstrap");
                }

                connectBan = new ConnectBanEnforcement(
                    events,
                    runtime.Moderation,
                    disconnect);
                communicationModeration = new ModerationCommunicationRuntime(
                    events,
                    runtime.Moderation,
                    runtime.Moderation);
                ChatSnapshotFormatter? snapshotFormatter = chatFormatSnapshots is null
                    ? null
                    : chatFormatSnapshots.TryFormat;
                chatModeration = new CounterStrikeChatModerationAdapter(
                    this,
                    new NativeChatRouter(
                        communicationModeration.ChatGate.Evaluate,
                        runtime.Players,
                        snapshotFormatter));
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

                if (playtime is not null)
                    playtimeTimer = AddTimer(5.0f,
                        () => Observe(playtime.CheckpointOnlineAsync(DateTimeOffset.UtcNow).AsTask(),
                            "playtime_checkpoint"), TimerFlags.REPEAT);

                foreach (var descriptor in runtime.Commands.GetCommands())
                {
                    bridge.Bind(descriptor);
                }

                if (anoVeto is not null)
                {
                    expiryTimer = AddTimer(
                        1.0f,
                        () => _ = ExpireAnoVetoAsync(anoVeto),
                        TimerFlags.REPEAT);
                }

                MenuPresenter = presenter;
                _adminCommands = adminCommands;
                _rankAdminCommands = rankAdminCommands;
                _rankAdminNotifications = rankAdminNotifications;
                rankAdminNotifications = null;
                _kickCommands = kickCommands;
                _connectBan = connectBan;
                _warningCommands = warningCommands;
                _extendedPlayerState = extendedPlayerState;
                _extendedAdminCommands = extendedAdminCommands;
                _extendedPositions = extendedPositions;
                _extendedPositionCommands = extendedPositionCommands;
                _extendedInventoryTeamCommands = extendedInventoryTeamCommands;
                _protectedServerControlCommands = protectedServerControlCommands;
                _communicationModeration = communicationModeration;
                _chatModeration = chatModeration;
                _chatFormatSnapshots = chatFormatSnapshots;
                _voiceModeration = voiceModeration;
                _commands = bridge;
                _runtime = runtime;
                _anoVeto = anoVeto;
                _playtime = playtime;
                _rank = rank;
                _chatFormatter = chatFormatter;
                _chatTags = chatTags;
                _combat = combat;
                _anoVetoExpiryTimer = expiryTimer;
                _voiceModerationTimer = voiceTimer;
                _playtimeTimer = playtimeTimer;
                _runtimeStatus = "ready";
                foreach (var player in runtime.Players.OnlinePlayers.ToArray())
                {
                    Observe(connectBan.CheckAsync(player, cancellationToken).AsTask(), "connect_ban_bootstrap");
                }

                Logger.LogInformation(
                    "AnoCore shared services ready; database/authorization initialized; AnoVeto {AnoVetoState}.",
                    anoVeto is null ? "disabled" : "active");
            }
            catch (Exception exception)
            {
                expiryTimer?.Kill();
                voiceTimer?.Kill();
                playtimeTimer?.Kill();
                playtime?.Dispose();
                rank?.Dispose();
                chatTags?.Dispose();
                combat?.Dispose();
                transitionMonitor?.Dispose();
                voiceModeration?.Dispose();
                chatFormatSnapshots?.Dispose();
                chatModeration?.Dispose();
                communicationModeration?.Dispose();
                anoVeto?.Dispose();
                bridge.Dispose();
                rankAdminCommands?.Dispose();
                rankAdminNotifications?.Dispose();
                warningCommands?.Dispose();
                protectedServerControlCommands?.Dispose();
                extendedInventoryTeamCommands?.Dispose();
                extendedPositionCommands?.Dispose();
                extendedAdminCommands?.Dispose();
                extendedPlayerState?.Dispose();
                adminCommands?.Dispose();
                kickCommands?.Dispose();
                connectBan?.Dispose();
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

    private async Task ExpireAnoVetoAsync(AnoVetoModuleRuntime anoVeto)
    {
        try
        {
            await anoVeto.ExpireAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "AnoCore runtime operation {Operation} failed.", "anoveto_expire");
        }
    }

    private ProtectedServerControlPolicy BuildProtectedServerControlPolicy(
        ProtectedServerControlConfiguration? configuration)
    {
        try
        {
            return ProtectedServerControlPolicy.Create(
                configuration ?? new ProtectedServerControlConfiguration());
        }
        catch (ArgumentException exception)
        {
            Logger.LogError(
                exception,
                "Protected server-control configuration is invalid; ConVar and server-command allow-lists are disabled.");
            return ProtectedServerControlPolicy.Create(
                new ProtectedServerControlConfiguration());
        }
    }

    public sealed class RuntimeConfiguration
    {
        public string ConnectionString { get; set; } = string.Empty;

        public ProtectedServerControlConfiguration ProtectedServerControls { get; set; } = new();
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
        RegisterEventHandler<EventWeaponFire>(OnWeaponFire);
        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
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
        DeregisterEventHandler<EventWeaponFire>(OnWeaponFire);
        DeregisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
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
                ExtendedAdministrationDisconnect.DisconnectAsync(
                    registry,
                    state,
                    positions,
                    current,
                    DateTimeOffset.UtcNow).AsTask(),
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

    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo _)
    {
        var combat = _combat;
        if (combat is null) return HookResult.Continue;

        try
        {
            var player = CombatPlayer(@event.Userid);
            if (player is null) return HookResult.Continue;

            var map = CombatDetailKey(Server.MapName, "unknown_map");
            var weapon = CombatDetailKey(@event.Weapon, "unknown");
            var eventId = CombatEventIdentity.CreateDetail(
                _combatServerInstance, map, CombatMapEpoch(), Server.TickCount,
                "weapon_fire", player.Id, null, weapon);
            var weaponFire = new CombatWeaponFireEvent(
                eventId, player.Id, DateTimeOffset.UtcNow, map, weapon);
            Observe(combat.RecordWeaponFireAsync(weaponFire).AsTask(), "combat_weapon_fire");
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Could not record combat weapon fire.");
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo _)
    {
        var combat = _combat;
        if (combat is null) return HookResult.Continue;

        try
        {
            var victim = CombatPlayer(@event.Userid);
            if (victim is null) return HookResult.Continue;

            var attacker = CombatPlayer(@event.Attacker);
            var teamDamage = attacker is not null && attacker.Id != victim.Id
                && victim.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist
                && attacker.Team == victim.Team;
            var map = CombatDetailKey(Server.MapName, "unknown_map");
            var weapon = CombatDetailKey(
                @event.Weapon, attacker is null ? "world" : "unknown");
            var signature = FormattableString.Invariant(
                $"{weapon}|{@event.Hitgroup}|{@event.DmgHealth}|{@event.DmgArmor}|"
                + $"{@event.Health}|{@event.Armor}");
            var eventId = CombatEventIdentity.CreateDetail(
                _combatServerInstance, map, CombatMapEpoch(), Server.TickCount,
                "player_hurt", victim.Id, attacker?.Id, signature);
            var damage = new CombatDamageEvent(
                eventId, victim.Id, attacker?.Id, DateTimeOffset.UtcNow,
                map, weapon, @event.Hitgroup, @event.DmgHealth, @event.DmgArmor, teamDamage);
            Observe(combat.RecordDamageAsync(damage).AsTask(), "combat_player_hurt");
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Could not record combat player damage.");
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo _)
    {
        RecordCombatDeath(@event);
        ReleaseExtendedStateForController(@event.Userid, "extended_admin_player_death");
        RefreshNextFrame(@event.Userid, "player_death");
        return HookResult.Continue;
    }

    private void RecordCombatDeath(EventPlayerDeath @event)
    {
        var combat = _combat;
        if (combat is null) return;
        try
        {
            var victim = CombatPlayer(@event.Userid);
            if (victim is null) return;
            var attacker = CombatPlayer(@event.Attacker);
            var assister = CombatPlayer(@event.Assister);
            var teamKill = attacker is not null && attacker.Id != victim.Id
                && victim.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist
                && attacker.Team == victim.Team;
            var eventId = CombatEventIdentity.Create(_combatServerInstance, Server.MapName,
                CombatMapEpoch(), Server.TickCount, victim.Id);
            var death = new AnoCore.Abstractions.Stats.CombatDeath(eventId, victim.Id,
                attacker?.Id, assister?.Id, DateTimeOffset.UtcNow, teamKill);
            Observe(combat.RecordAsync(death).AsTask(), "combat_death");
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Could not record combat death.");
        }
    }

    private static long CombatMapEpoch()
        => checked((long)Math.Round(Server.EngineTime - Server.CurrentTime));

    private static string CombatDetailKey(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return new string(normalized
            .Where(character => !char.IsControl(character))
            .Take(128)
            .ToArray());
    }

    private PlayerSnapshot? CombatPlayer(CCSPlayerController? controller)
    {
        if (controller is not { IsValid: true, IsBot: false, IsHLTV: false }
            || controller.SteamID == 0 || _players is null)
            return null;
        var id = new PlayerId(controller.SteamID);
        return _players.TryGet(id, out var player) && player?.IsConnected == true
            ? player : null;
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
