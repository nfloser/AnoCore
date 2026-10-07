using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Management;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Placeholders;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Abstractions.Voting;
using AnoCore.Modules.Admin;
using AnoCore.Modules.AnoVeto;
using AnoCore.Modules.Progression;
using AnoCore.Modules.Progression.Persistence;
using AnoCore.Modules.Stats;
using AnoCore.Modules.Tournament;
using AnoCore.Modules.Tournament.Persistence;
using AnoCore.Plugin.Administration;
using AnoCore.Plugin.Commands;
using AnoCore.Plugin.Hud;
using AnoCore.Plugin.Maps;
using AnoCore.Plugin.Menus;
using AnoCore.Plugin.Messaging;
using AnoCore.Plugin.Moderation;
using AnoCore.Plugin.Players;
using AnoCore.Plugin.Tournament;
using AnoCore.Runtime.Composition;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Management;
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
    private ManagementPipeServer? _pendingManagementPipe;
    private AnoVetoModuleRuntime? _pendingAnoVeto;
    private PlaytimeModule? _pendingPlaytime;
    private RankModule? _pendingRank;
    private GameplayStatsModule? _pendingGameplayStats;
    private AchievementModule? _pendingAchievements;
    private ChallengeModule? _pendingChallenges;
    private GameplayXpModule? _pendingGameplayXp;
    private SeasonModule? _pendingSeasons;
    private TournamentMatchRuntime? _pendingTournamentMatch;
    private ChatMessageFormatter? _pendingChatFormatter;
    private SelectableChatTagModule? _pendingChatTags;
    private ProtectedServerControlPolicy? _pendingProtectedServerControlPolicy;
    private RuntimeServices? _runtime;
    private ManagementPipeServer? _managementPipe;
    private AnoVetoModuleRuntime? _anoVeto;
    private PlaytimeModule? _playtime;
    private RankModule? _rank;
    private LiveRankScoringService? _liveRankScoring;
    private long _rankRoundGeneration;
    private GameplayStatsModule? _gameplayStats;
    private AchievementModule? _achievements;
    private ChallengeModule? _challenges;
    private GameplayXpModule? _gameplayXp;
    private SeasonModule? _seasons;
    private TournamentMatchRuntime? _tournamentMatch;
    private TournamentTeamEnforcement? _tournamentTeamEnforcement;
    private TournamentSpectatorPolicySource? _tournamentSpectatorPolicies;
    private TournamentSpectatorEnforcement? _tournamentSpectatorEnforcement;
    private TournamentCommandController? _tournamentCommands;
    private TournamentMapSelectionCommandController? _tournamentMapSelectionCommands;
    private ChatMessageFormatter? _chatFormatter;
    private SelectableChatTagModule? _chatTags;
    private ChatFormatSnapshotLifecycle? _chatFormatSnapshots;
    private CombatModule? _combat;
    private string _combatServerInstance = string.Empty;
    private bool _roundFirstBloodRecorded;
    private ModerationCommandController? _adminCommands;
    private RankAdjustmentCommandController? _rankAdminCommands;
    private RankAdjustmentNotificationService? _rankAdminNotifications;
    private StatisticsResetCommandController? _statisticsResetCommands;
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
    private IDisposable? _messageTransportRegistration;
    private CounterStrikeCustomHudService? _customHud;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _anoVetoExpiryTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _voiceModerationTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _playtimeTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _achievementTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _challengeTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _gameplayXpTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _seasonTimer;
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

            _managementPipe?.Dispose();
            _managementPipe = null;
            _pendingManagementPipe?.Dispose();
            _pendingManagementPipe = null;

            _anoVetoExpiryTimer?.Kill();
            _anoVetoExpiryTimer = null;
            _voiceModerationTimer?.Kill();
            _voiceModerationTimer = null;
            _playtimeTimer?.Kill();
            _playtimeTimer = null;
            _achievementTimer?.Kill();
            _achievementTimer = null;
            _challengeTimer?.Kill();
            _challengeTimer = null;
            _gameplayXpTimer?.Kill();
            _seasonTimer?.Kill();
            _gameplayXpTimer = null;
            _seasonTimer = null;
            _gameplayXp?.Dispose();
            _seasons?.Dispose();
            _gameplayXp = null;
            _seasons = null;
            _challenges?.Dispose();
            _challenges = null;
            _achievements?.Dispose();
            _achievements = null;
            if (_playtime is not null)
                Observe(_playtime.CheckpointOnlineAsync(DateTimeOffset.UtcNow).AsTask(), "playtime_unload");
            _playtime?.Dispose();
            _playtime = null;
            _chatFormatSnapshots?.Dispose();
            _chatFormatSnapshots = null;
            _chatTags?.Dispose();
            _chatTags = null;
            _liveRankScoring?.Dispose();
            _liveRankScoring = null;
            _rank?.Dispose();
            _rank = null;
            _gameplayStats?.Dispose();
            _gameplayStats = null;
            _tournamentMapSelectionCommands?.Dispose();
            _tournamentMapSelectionCommands = null;
            _tournamentCommands?.Dispose();
            _tournamentCommands = null;
            _tournamentSpectatorEnforcement?.Dispose();
            _tournamentSpectatorEnforcement = null;
            _tournamentSpectatorPolicies?.Dispose();
            _tournamentSpectatorPolicies = null;
            _tournamentTeamEnforcement?.Dispose();
            _tournamentTeamEnforcement = null;
            _tournamentMatch = null;
            _chatFormatter = null;
            _combat?.Dispose();
            _combat = null;
            _pendingPlaytime?.Dispose();
            _pendingPlaytime = null;
            _pendingRank?.Dispose();
            _pendingRank = null;
            _pendingGameplayStats?.Dispose();
            _pendingAchievements?.Dispose();
            _pendingChallenges?.Dispose();
            _pendingGameplayXp?.Dispose();
            _pendingSeasons?.Dispose();
            _pendingGameplayStats = null;
            _pendingAchievements = null;
            _pendingChallenges = null;
            _pendingGameplayXp = null;
            _pendingSeasons = null;
            _pendingTournamentMatch = null;
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
            _statisticsResetCommands?.Dispose();
            _statisticsResetCommands = null;
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
            _messageTransportRegistration?.Dispose();
            _messageTransportRegistration = null;
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
        ManagementPipeServer? createdManagementPipe = null;
        AnoVetoModuleRuntime? createdAnoVeto = null;
        PlaytimeModule? createdPlaytime = null;
        RankModule? createdRank = null;
        GameplayStatsModule? createdGameplayStats = null;
        AchievementModule? createdAchievements = null;
        ChallengeModule? createdChallenges = null;
        GameplayXpModule? createdGameplayXp = null;
        SeasonModule? createdSeasons = null;
        TournamentMatchRuntime? createdTournamentMatch = null;
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
            var managementConfiguration = await configuration.LoadAsync(
                "management",
                () => new ManagementBridgeConfiguration(),
                ManagementBridgeConfiguration.Validate,
                cancellationToken).ConfigureAwait(false);
            var managementRateLimiter = new ManagementRateLimiter(
                managementConfiguration.RateLimits());
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
                cancellationToken: timeout.Token,
                managementRateLimiter: managementRateLimiter).ConfigureAwait(false);

            if (managementConfiguration.Enabled)
            {
                var authenticator = new ManagementTokenAuthenticator(
                    managementConfiguration.BuildCredentials());
                var gateway = new ManagementApiGateway(
                    authenticator,
                    created.ManagementCapabilities,
                    created.ManagementStatus,
                    created.ManagementRateLimiter);
                createdManagementPipe = new ManagementPipeServer(
                    managementConfiguration.PipeName,
                    new ManagementHttpAdapter(gateway),
                    exception => Logger.LogError(
                        exception,
                        "AnoCore management pipe request failed."));
            }

            try
            {
                var database = created.GetService(typeof(IDatabase)) as IDatabase
                    ?? throw new InvalidOperationException(
                        "AnoCore runtime did not provide the shared database service.");
                await TournamentPersistenceBootstrap.EnsureReadyAsync(database, timeout.Token)
                    .ConfigureAwait(false);
                createdTournamentMatch = await TournamentMatchRuntime.CreateAsync(
                    new TournamentRecoveryService(
                        new MySqlTournamentMatchRepository(database)),
                    timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                createdTournamentMatch = null;
                Logger.LogError(
                    exception,
                    "Tournament recovery failed; AnoCore will continue without tournament team enforcement.");
            }

            try
            {
                PlaytimeNotificationService? playtimeNotifications = null;
                try
                {
                    playtimeNotifications = await PlaytimeNotificationService.CreateAsync(
                        configuration, players, created.Playtime, created.Settings,
                        created.ToggleCatalog, created.Messages,
                        exception => Logger.LogError(exception, "Playtime notification failed."),
                        timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Logger.LogError(exception,
                        "Playtime notification composition failed; durable tracking remains active.");
                }
                try
                {
                    createdPlaytime = await PlaytimeModule.CreateAsync(
                        events, players, created.Playtime, created.Commands,
                        cancellationToken: timeout.Token,
                        notifications: playtimeNotifications).ConfigureAwait(false);
                }
                catch
                {
                    playtimeNotifications?.Dispose();
                    throw;
                }
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
                ILeetifyProfileProvider? leetify = null;
                var leetifyApiKey =
                    Environment.GetEnvironmentVariable("ANOCORE_LEETIFY_API_KEY");
                if (!string.IsNullOrWhiteSpace(leetifyApiKey))
                {
                    try
                    {
                        leetify = new LeetifyHttpProfileProvider(leetifyApiKey);
                    }
                    catch (ArgumentException)
                    {
                        Logger.LogWarning(
                            "Leetify integration configuration is invalid; "
                            + "internal AnoRating remains available.");
                    }
                }

                createdGameplayStats = await GameplayStatsModule.CreateAsync(
                    configuration,
                    created.Commands,
                    players,
                    created.GameplayStats,
                    created.Combat,
                    created.Menus,
                    events,
                    timeout.Token,
                    leetify).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                createdGameplayStats?.Dispose();
                createdGameplayStats = null;
                Logger.LogError(exception,
                    "Gameplay statistics composition failed; AnoCore will continue without extended gameplay stats.");
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

            try
            {
                var achievementConfiguration = await configuration.LoadAsync("achievements",
                    () => new AchievementConfiguration(), AchievementConfiguration.Validate, timeout.Token).ConfigureAwait(false);
                if (achievementConfiguration.Enabled)
                {
                    var database = (IDatabase)created.GetService(typeof(IDatabase))!;
                    await ProgressionPersistenceBootstrap.EnsureReadyAsync(database, timeout.Token).ConfigureAwait(false);
                    createdAchievements = new AchievementModule(achievementConfiguration.Snapshot(), players,
                        created.GameplayStats, new MySqlAchievementRepository(database),
                        new MySqlProgressionGrantRepository(database), created.Commands,
                        exception => Logger.LogError(exception, "Achievement checkpoint failed."),
                        created.Settings, created.ToggleCatalog, created.Messages);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                createdAchievements?.Dispose();
                createdAchievements = null;
                Logger.LogError(exception, "Achievement composition failed; other AnoCore modules continue.");
            }

            try
            {
                var challengeConfiguration = await configuration.LoadAsync("challenges",
                    () => new ChallengeConfiguration(), ChallengeConfiguration.Validate, timeout.Token).ConfigureAwait(false);
                if (challengeConfiguration.Enabled)
                {
                    var xpConfiguration = await configuration.LoadAsync("achievements",
                        () => new AchievementConfiguration(), AchievementConfiguration.Validate, timeout.Token).ConfigureAwait(false);
                    var database = (IDatabase)created.GetService(typeof(IDatabase))!;
                    await ProgressionPersistenceBootstrap.EnsureReadyAsync(database, timeout.Token).ConfigureAwait(false);
                    createdChallenges = new ChallengeModule(challengeConfiguration.Snapshot(), xpConfiguration.Snapshot().Xp,
                        players, new MySqlChallengeRepository(database), created.Commands,
                        reportError: exception => Logger.LogError(exception, "Challenge checkpoint failed."),
                        settings: created.Settings, toggles: created.ToggleCatalog, messages: created.Messages);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                createdChallenges?.Dispose();
                createdChallenges = null;
                Logger.LogError(exception, "Challenge composition failed; other AnoCore modules continue.");
            }

            try
            {
                var gameplayXpConfiguration = await configuration.LoadAsync("gameplay-xp",
                    () => new GameplayXpConfiguration(), GameplayXpConfiguration.Validate, timeout.Token).ConfigureAwait(false);
                if (gameplayXpConfiguration.Enabled)
                {
                    var xpConfiguration = await configuration.LoadAsync("achievements",
                        () => new AchievementConfiguration(), AchievementConfiguration.Validate, timeout.Token).ConfigureAwait(false);
                    var database = (IDatabase)created.GetService(typeof(IDatabase))!;
                    await ProgressionPersistenceBootstrap.EnsureReadyAsync(database, timeout.Token).ConfigureAwait(false);
                    createdGameplayXp = new GameplayXpModule(gameplayXpConfiguration.Snapshot(), xpConfiguration.Snapshot().Xp,
                        players, new MySqlGameplayXpRepository(database), new MySqlProgressionGrantRepository(database), created.Commands,
                        exception => Logger.LogError(exception, "Gameplay XP checkpoint failed."));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                createdGameplayXp?.Dispose();
                createdGameplayXp = null;
                Logger.LogError(exception, "Gameplay XP composition failed; other AnoCore modules continue.");
            }

            try
            {
                var seasonConfiguration = await configuration.LoadAsync("seasons",
                    () => new SeasonConfiguration(), SeasonConfiguration.Validate, timeout.Token).ConfigureAwait(false);
                if (seasonConfiguration.Enabled)
                {
                    var xpConfiguration = await configuration.LoadAsync("achievements",
                        () => new AchievementConfiguration(), AchievementConfiguration.Validate, timeout.Token).ConfigureAwait(false);
                    var database = (IDatabase)created.GetService(typeof(IDatabase))!;
                    await ProgressionPersistenceBootstrap.EnsureReadyAsync(database, timeout.Token).ConfigureAwait(false);
                    createdSeasons = await SeasonModule.CreateAsync(seasonConfiguration.Snapshot(), xpConfiguration.Snapshot().Xp,
                        players, new MySqlSeasonRepository(database), new MySqlSeasonProgressionRepository(database),
                        new MySqlSeasonRewardRepository(database), created.Commands, DateTimeOffset.UtcNow,
                        reportError: exception => Logger.LogError(exception, "Season reward checkpoint failed."),
                        cancellationToken: timeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                createdSeasons?.Dispose();
                createdSeasons = null;
                Logger.LogError(exception, "Season composition failed; other AnoCore modules continue.");
            }

            lock (_startupGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _pendingRuntime = created;
                _pendingManagementPipe = createdManagementPipe;
                _pendingAnoVeto = createdAnoVeto;
                _pendingPlaytime = createdPlaytime;
                _pendingRank = createdRank;
                _pendingGameplayStats = createdGameplayStats;
                _pendingAchievements = createdAchievements;
                _pendingChallenges = createdChallenges;
                _pendingGameplayXp = createdGameplayXp;
                _pendingSeasons = createdSeasons;
                _pendingTournamentMatch = createdTournamentMatch;
                _pendingChatFormatter = createdChatFormatter;
                _pendingChatTags = createdChatTags;
                _pendingProtectedServerControlPolicy = protectedServerControlPolicy;
                created = null;
                createdManagementPipe = null;
                createdAnoVeto = null;
                createdPlaytime = null;
                createdRank = null;
                createdGameplayStats = null;
                createdAchievements = null;
                createdChallenges = null;
                createdGameplayXp = null;
                createdSeasons = null;
                createdTournamentMatch = null;
                createdChatFormatter = null;
                createdChatTags = null;
                Server.NextWorldUpdate(() => ActivateRuntime(cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            createdManagementPipe?.Dispose();
            createdAnoVeto?.Dispose();
            createdPlaytime?.Dispose();
            createdRank?.Dispose();
            createdGameplayStats?.Dispose();
            createdAchievements?.Dispose();
            createdChallenges?.Dispose();
            createdGameplayXp?.Dispose();
            createdSeasons?.Dispose();
            createdChatTags?.Dispose();
            created?.Dispose();
        }
        catch (Exception exception)
        {
            createdManagementPipe?.Dispose();
            createdAnoVeto?.Dispose();
            createdPlaytime?.Dispose();
            createdRank?.Dispose();
            createdGameplayStats?.Dispose();
            createdAchievements?.Dispose();
            createdChallenges?.Dispose();
            createdGameplayXp?.Dispose();
            createdSeasons?.Dispose();
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
                    _pendingGameplayStats?.Dispose();
                    _pendingAchievements?.Dispose();
                    _pendingChallenges?.Dispose();
                    _pendingGameplayXp?.Dispose();
                    _pendingSeasons?.Dispose();
                    _pendingGameplayStats = null;
                    _pendingAchievements = null;
                    _pendingChallenges = null;
                    _pendingGameplayXp = null;
                    _pendingSeasons = null;
                    _pendingTournamentMatch = null;
                    _pendingChatFormatter = null;
                    _pendingChatTags?.Dispose();
                    _pendingChatTags = null;
                    _pendingAnoVeto?.Dispose();
                    _pendingAnoVeto = null;
                    _pendingManagementPipe?.Dispose();
                    _pendingManagementPipe = null;
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
            var managementPipe = _pendingManagementPipe;
            var anoVeto = _pendingAnoVeto;
            var playtime = _pendingPlaytime;
            var rank = _pendingRank;
            var gameplayStats = _pendingGameplayStats;
            var achievements = _pendingAchievements;
            var challenges = _pendingChallenges;
            var gameplayXp = _pendingGameplayXp;
            var seasons = _pendingSeasons;
            var tournamentMatch = _pendingTournamentMatch;
            var chatFormatter = _pendingChatFormatter;
            var chatTags = _pendingChatTags;
            _pendingRuntime = null;
            _pendingManagementPipe = null;
            _pendingAnoVeto = null;
            _pendingPlaytime = null;
            _pendingRank = null;
            _pendingGameplayStats = null;
            _pendingAchievements = null;
            _pendingChallenges = null;
            _pendingGameplayXp = null;
            _pendingSeasons = null;
            _pendingTournamentMatch = null;
            _pendingChatFormatter = null;
            _pendingChatTags = null;
            var protectedServerControlPolicy = _pendingProtectedServerControlPolicy
                ?? ProtectedServerControlPolicy.Create(
                    new ProtectedServerControlConfiguration());
            _pendingProtectedServerControlPolicy = null;
            ModerationCommandController? adminCommands = null;
            RankAdjustmentCommandController? rankAdminCommands = null;
            RankAdjustmentNotificationService? rankAdminNotifications = null;
            StatisticsResetCommandController? statisticsResetCommands = null;
            RankTransitionMonitor? transitionMonitor = null;
            LiveRankScoringService? liveRankScoring = null;
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
            TournamentTeamEnforcement? tournamentTeamEnforcement = null;
            TournamentSpectatorPolicySource? tournamentSpectatorPolicies = null;
            TournamentSpectatorEnforcement? tournamentSpectatorEnforcement = null;
            TournamentCommandController? tournamentCommands = null;
            TournamentMapSelectionCommandController? tournamentMapSelectionCommands = null;
            IDisposable? messageTransportRegistration = null;
            var presenter = new CounterStrikeMenuPresenter(this, runtime.Menus, Logger);
            var bridge = new CounterStrikeCommandBridge(
                this,
                runtime.Commands,
                Logger,
                (commandName, player) =>
                {
                    if (string.Equals(commandName, RankModule.MenuCommandName,
                            StringComparison.Ordinal)
                        || string.Equals(commandName, GameplayStatsModule.MenuCommandName,
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
            CounterStrikeSharp.API.Modules.Timers.Timer? achievementTimer = null;
            CounterStrikeSharp.API.Modules.Timers.Timer? challengeTimer = null;
            CounterStrikeSharp.API.Modules.Timers.Timer? gameplayXpTimer = null;
            CounterStrikeSharp.API.Modules.Timers.Timer? seasonTimer = null;

            try
            {
                messageTransportRegistration = runtime.Messages.AttachTransport(
                    new CounterStrikeMessageTransport(runtime.Players));
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
                statisticsResetCommands = new StatisticsResetCommandController(
                    runtime.Commands,
                    new StatisticsResetCommandExecutor(
                        targetGateway, runtime.StatisticsResetAdministration));
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

                transitionMonitor = rank is null || rank.Configuration.Source == RankScoreSource.EventLedger
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
                if (rank is not null && gameplayStats is not null
                    && rank.Configuration.Source == RankScoreSource.DerivedStatistics)
                    gameplayStats.EnableRankTracking(
                        rank.Configuration,
                        runtime.Combat,
                        new RankNotificationPreferenceSink(
                            runtime.Settings,
                            new CounterStrikeRankTransitionNotifier(runtime.Players),
                            exception => Logger.LogError(
                                exception, "Rank notification preference read failed.")),
                        rankScoreChanges,
                        exception => Logger.LogError(exception, "Gameplay rank presentation failed."));
                if (rank is not null && rank.Configuration.Source == RankScoreSource.EventLedger)
                    liveRankScoring = new LiveRankScoringService(rank.Configuration,
                        runtime.RankPointEvents, runtime.Combat, runtime.Players, runtime.Authorization,
                        new RankNotificationPreferenceSink(runtime.Settings,
                            new CounterStrikeRankTransitionNotifier(runtime.Players),
                            exception => Logger.LogError(exception, "Live rank notification preference failed.")),
                        rankScoreChanges,
                        exception => Logger.LogError(exception, "Live rank presentation failed."));
                combat = new CombatModule(
                    runtime.Commands, runtime.Players, runtime.Combat, transitionMonitor);
                transitionMonitor = null;
                var events = _eventBus
                    ?? throw new InvalidOperationException("AnoCore event bus is unavailable during activation.");
                if (tournamentMatch is not null)
                {
                    var database = runtime.GetService(typeof(IDatabase)) as IDatabase
                        ?? throw new InvalidOperationException(
                            "AnoCore runtime did not provide the shared database service.");
                    var tournamentConfiguration = runtime.GetService(typeof(IConfigStore))
                        as IConfigStore
                        ?? throw new InvalidOperationException(
                            "AnoCore runtime did not provide the shared configuration service.");
                    var recovery = new TournamentRecoveryService(
                        new MySqlTournamentMatchRepository(database));
                    tournamentSpectatorPolicies = new TournamentSpectatorPolicySource();
                    tournamentCommands = new TournamentCommandController(
                        tournamentConfiguration,
                        runtime.Commands,
                        runtime.Players,
                        recovery,
                        tournamentMatch,
                        runtime.AdminAudit,
                        spectatorPolicies: tournamentSpectatorPolicies);
                    if (anoVeto is not null)
                    {
                        tournamentMapSelectionCommands =
                            new TournamentMapSelectionCommandController(
                                runtime.Commands,
                                new TournamentMapSelectionService(
                                    recovery,
                                    tournamentMatch,
                                    new AnoVetoTournamentMapSelectionSource(
                                        anoVeto.Coordinator)));
                    }
                    tournamentTeamEnforcement = new TournamentTeamEnforcement(
                        events,
                        runtime.Players,
                        tournamentMatch,
                        new CounterStrikeTournamentTeamTransport(runtime.Players),
                        (exception, player) => Logger.LogError(
                            exception,
                            "Tournament team enforcement failed for {PlayerId} session {SessionId}.",
                            player.Id,
                            player.SessionId));
                    tournamentSpectatorEnforcement = new TournamentSpectatorEnforcement(
                        events,
                        runtime.Players,
                        tournamentMatch,
                        tournamentSpectatorPolicies,
                        new CounterStrikeTournamentSpectatorTransport(
                            runtime.Players,
                            disconnect),
                        (exception, player) => Logger.LogError(
                            exception,
                            "Tournament spectator enforcement failed for {PlayerId} session {SessionId}.",
                            player.Id,
                            player.SessionId));
                    Observe(
                        tournamentTeamEnforcement.ReconcileOnlineAsync(cancellationToken).AsTask(),
                        "tournament_team_bootstrap");
                    Observe(
                        InitializeTournamentSpectatorPolicyAsync(
                            tournamentConfiguration,
                            tournamentMatch,
                            tournamentSpectatorPolicies,
                            tournamentSpectatorEnforcement,
                            cancellationToken),
                        "tournament_spectator_bootstrap");
                }

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

                if (gameplayXp is not null)
                {
                    gameplayXpTimer = AddTimer(gameplayXp.CheckpointSeconds,
                        () => Observe(gameplayXp.ReconcileOnlineAsync(DateTimeOffset.UtcNow).AsTask(),
                            "gameplay_xp_checkpoint"), TimerFlags.REPEAT);
                }

                if (seasons is not null)
                {
                    seasonTimer = AddTimer(seasons.CheckpointSeconds,
                        () => Observe(seasons.ReconcileAsync(DateTimeOffset.UtcNow).AsTask(),
                            "season_reward_checkpoint"), TimerFlags.REPEAT);
                }

                if (challenges is not null)
                {
                    challengeTimer = AddTimer(challenges.CheckpointSeconds,
                        () => Observe(challenges.ReconcileOnlineAsync(DateTimeOffset.UtcNow).AsTask(),
                            "challenge_checkpoint"), TimerFlags.REPEAT);
                }

                if (achievements is not null)
                {
                    achievementTimer = AddTimer(achievements.CheckpointSeconds,
                        () => Observe(achievements.ReconcileOnlineAsync(DateTimeOffset.UtcNow).AsTask(),
                            "achievement_checkpoint"), TimerFlags.REPEAT);
                }

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

                managementPipe?.Start();

                MenuPresenter = presenter;
                _adminCommands = adminCommands;
                _rankAdminCommands = rankAdminCommands;
                _rankAdminNotifications = rankAdminNotifications;
                rankAdminNotifications = null;
                _statisticsResetCommands = statisticsResetCommands;
                statisticsResetCommands = null;
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
                _messageTransportRegistration = messageTransportRegistration;
                messageTransportRegistration = null;
                _runtime = runtime;
                _managementPipe = managementPipe;
                _anoVeto = anoVeto;
                _playtime = playtime;
                _rank = rank;
                _liveRankScoring = liveRankScoring;
                _gameplayStats = gameplayStats;
                _achievements = achievements;
                _challenges = challenges;
                _gameplayXp = gameplayXp;
                _seasons = seasons;
                _tournamentMatch = tournamentMatch;
                _tournamentTeamEnforcement = tournamentTeamEnforcement;
                _tournamentSpectatorPolicies = tournamentSpectatorPolicies;
                _tournamentSpectatorEnforcement = tournamentSpectatorEnforcement;
                _tournamentCommands = tournamentCommands;
                _tournamentMapSelectionCommands = tournamentMapSelectionCommands;
                _chatFormatter = chatFormatter;
                _chatTags = chatTags;
                _combat = combat;
                _anoVetoExpiryTimer = expiryTimer;
                _voiceModerationTimer = voiceTimer;
                _playtimeTimer = playtimeTimer;
                _achievementTimer = achievementTimer;
                _challengeTimer = challengeTimer;
                _gameplayXpTimer = gameplayXpTimer;
                _seasonTimer = seasonTimer;
                _runtimeStatus = "ready";
                if (seasons is not null)
                    Observe(seasons.ReconcileAsync(DateTimeOffset.UtcNow).AsTask(), "season_reward_bootstrap");
                if (gameplayXp is not null)
                    Observe(gameplayXp.ReconcileOnlineAsync(DateTimeOffset.UtcNow).AsTask(), "gameplay_xp_bootstrap");
                if (challenges is not null)
                    Observe(challenges.ReconcileOnlineAsync(DateTimeOffset.UtcNow).AsTask(), "challenge_bootstrap");
                if (achievements is not null)
                    Observe(achievements.ReconcileOnlineAsync(DateTimeOffset.UtcNow).AsTask(), "achievement_bootstrap");
                foreach (var player in runtime.Players.OnlinePlayers.ToArray())
                {
                    Observe(connectBan.CheckAsync(player, cancellationToken).AsTask(), "connect_ban_bootstrap");
                }

                Logger.LogInformation(
                    "AnoCore shared services ready; database/authorization initialized; AnoVeto {AnoVetoState}; management pipe {ManagementState}.",
                    anoVeto is null ? "disabled" : "active",
                    managementPipe is null ? "disabled" : "active");
            }
            catch (Exception exception)
            {
                expiryTimer?.Kill();
                voiceTimer?.Kill();
                playtimeTimer?.Kill();
                achievementTimer?.Kill();
                challengeTimer?.Kill();
                gameplayXpTimer?.Kill();
                seasonTimer?.Kill();
                playtime?.Dispose();
                liveRankScoring?.Dispose();
                if (ReferenceEquals(_liveRankScoring, liveRankScoring)) _liveRankScoring = null;
                rank?.Dispose();
                gameplayStats?.Dispose();
                achievements?.Dispose();
                challenges?.Dispose();
                gameplayXp?.Dispose();
                seasons?.Dispose();
                tournamentMapSelectionCommands?.Dispose();
                tournamentCommands?.Dispose();
                tournamentSpectatorEnforcement?.Dispose();
                tournamentSpectatorPolicies?.Dispose();
                tournamentTeamEnforcement?.Dispose();
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
                statisticsResetCommands?.Dispose();
                warningCommands?.Dispose();
                protectedServerControlCommands?.Dispose();
                extendedInventoryTeamCommands?.Dispose();
                extendedPositionCommands?.Dispose();
                extendedAdminCommands?.Dispose();
                extendedPlayerState?.Dispose();
                adminCommands?.Dispose();
                kickCommands?.Dispose();
                connectBan?.Dispose();
                messageTransportRegistration?.Dispose();
                managementPipe?.Dispose();
                runtime.Dispose();
                MenuPresenter = null;
                _runtimeStatus = "activation failed";
                Logger.LogError(exception, "AnoCore command/menu/module activation failed.");
            }
        }
    }

    private async Task InitializeTournamentSpectatorPolicyAsync(
        IConfigStore configuration,
        TournamentMatchRuntime runtime,
        TournamentSpectatorPolicySource policies,
        TournamentSpectatorEnforcement enforcement,
        CancellationToken cancellationToken)
    {
        TournamentSpectatorPolicy? policy = null;
        try
        {
            var active = runtime.CurrentSession;
            if (active is not null)
            {
                var definition = await configuration.LoadAsync(
                    TournamentCommandController.DefinitionConfigName,
                    () => TournamentMatchDefinition.Default,
                    TournamentMatchDefinition.Validate,
                    cancellationToken).ConfigureAwait(false);
                if (definition.Enabled
                    && Guid.TryParse(definition.MatchId, out var configuredMatch)
                    && configuredMatch == active.Machine.Configuration.MatchId)
                {
                    policy = definition.ToSpectatorPolicy();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.LogError(
                exception,
                "Tournament spectator policy could not be restored; spectator enforcement stays disabled.");
        }

        policies.Replace(policy);
        await enforcement.ReconcileOnlineAsync(cancellationToken).ConfigureAwait(false);
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
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventGrenadeThrown>(OnGrenadeThrown);
        RegisterEventHandler<EventBombPlanted>(OnBombPlanted);
        RegisterEventHandler<EventBombDefused>(OnBombDefused);
        RegisterEventHandler<EventHostageRescued>(OnHostageRescued);
        RegisterEventHandler<EventHostageKilled>(OnHostageKilled);
        RegisterEventHandler<EventRoundMvp>(OnRoundMvp);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventCsWinPanelMatch>(OnMatchEnd);
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
        DeregisterEventHandler<EventRoundStart>(OnRoundStart);
        DeregisterEventHandler<EventGrenadeThrown>(OnGrenadeThrown);
        DeregisterEventHandler<EventBombPlanted>(OnBombPlanted);
        DeregisterEventHandler<EventBombDefused>(OnBombDefused);
        DeregisterEventHandler<EventHostageRescued>(OnHostageRescued);
        DeregisterEventHandler<EventHostageKilled>(OnHostageKilled);
        DeregisterEventHandler<EventRoundMvp>(OnRoundMvp);
        DeregisterEventHandler<EventRoundEnd>(OnRoundEnd);
        DeregisterEventHandler<EventCsWinPanelMatch>(OnMatchEnd);
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
        if (combat is null || !GameplayStatsAllowed()) return HookResult.Continue;

        try
        {
            var player = CombatPlayer(@event.Userid);
            if (player is null) return HookResult.Continue;

            var map = CombatDetailKey(Server.MapName, "unknown_map", 128);
            var weapon = CombatDetailKey(@event.Weapon, "unknown", 64);
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
        if (combat is null || !GameplayStatsAllowed()) return HookResult.Continue;

        try
        {
            var victim = CombatPlayer(@event.Userid);
            if (victim is null) return HookResult.Continue;

            var attacker = CombatPlayer(@event.Attacker);
            var teamDamage = !(_gameplayStats?.Configuration.FreeForAll ?? false)
                && attacker is not null && attacker.Id != victim.Id
                && victim.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist
                && attacker.Team == victim.Team;
            var map = CombatDetailKey(Server.MapName, "unknown_map", 128);
            var weapon = CombatDetailKey(
                @event.Weapon, attacker is null ? "world" : "unknown", 64);
            var signature = FormattableString.Invariant(
                $"{weapon}|{@event.Hitgroup}|{@event.DmgHealth}|{@event.DmgArmor}|{@event.Health}|{@event.Armor}");
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
        RecordLiveRankDeath(@event);
        RecordCombatDeath(@event);
        ReleaseExtendedStateForController(@event.Userid, "extended_admin_player_death");
        RefreshNextFrame(@event.Userid, "player_death");
        return HookResult.Continue;
    }

    private void RecordCombatDeath(EventPlayerDeath @event)
    {
        var combat = _combat;
        if (combat is null || !GameplayStatsAllowed()) return;
        try
        {
            var victim = CombatPlayer(@event.Userid);
            if (victim is null) return;
            var attacker = CombatPlayer(@event.Attacker);
            var assister = CombatPlayer(@event.Assister);
            var freeForAll = _gameplayStats?.Configuration.FreeForAll ?? false;
            var teamKill = !freeForAll
                && attacker is not null && attacker.Id != victim.Id
                && victim.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist
                && attacker.Team == victim.Team;
            var eventId = CombatEventIdentity.Create(_combatServerInstance, Server.MapName,
                CombatMapEpoch(), Server.TickCount, victim.Id);
            var death = new AnoCore.Abstractions.Stats.CombatDeath(eventId, victim.Id,
                attacker?.Id, assister?.Id, DateTimeOffset.UtcNow, teamKill);
            Observe(combat.RecordAsync(death).AsTask(), "combat_death");

            var validKill = attacker is not null
                && attacker.Id != victim.Id
                && !teamKill;
            if (validKill)
            {
                var victimSignature = victim.Id.SteamId64.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                if (!_roundFirstBloodRecorded)
                {
                    _roundFirstBloodRecorded = true;
                    RecordGameplayStat(
                        @event.Attacker, GameplayStatKind.FirstBlood, victimSignature);
                }

                if (@event.Headshot)
                    RecordGameplayStat(
                        @event.Attacker, GameplayStatKind.HeadshotKill, victimSignature);
                if (@event.Noscope)
                    RecordGameplayStat(
                        @event.Attacker, GameplayStatKind.NoScopeKill, victimSignature);
                if (@event.Penetrated > 0)
                    RecordGameplayStat(
                        @event.Attacker, GameplayStatKind.PenetratedKill, victimSignature);
                if (@event.Thrusmoke)
                    RecordGameplayStat(
                        @event.Attacker, GameplayStatKind.ThroughSmokeKill, victimSignature);
                if (@event.Attackerblind)
                    RecordGameplayStat(
                        @event.Attacker, GameplayStatKind.FlashedKill, victimSignature);
                if (@event.Dominated > 0)
                    RecordGameplayStat(
                        @event.Attacker, GameplayStatKind.DominatedKill, victimSignature);
                if (@event.Revenge > 0)
                    RecordGameplayStat(
                        @event.Attacker, GameplayStatKind.RevengeKill, victimSignature);
                if (@event.Assistedflash && assister is not null)
                    RecordGameplayStat(
                        @event.Assister, GameplayStatKind.FlashAssist, victimSignature);
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Could not record combat death.");
        }
    }


    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo _)
    {
        Interlocked.Increment(ref _rankRoundGeneration);
        _roundFirstBloodRecorded = false;
        return HookResult.Continue;
    }

    private HookResult OnGrenadeThrown(EventGrenadeThrown @event, GameEventInfo _)
    {
        RecordGameplayStat(@event.Userid, GameplayStatKind.GrenadeThrown, "grenade");
        return HookResult.Continue;
    }

    private HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo _)
    {
        RecordGameplayStat(@event.Userid, GameplayStatKind.BombPlanted, "bomb_planted");
        return HookResult.Continue;
    }

    private HookResult OnBombDefused(EventBombDefused @event, GameEventInfo _)
    {
        RecordGameplayStat(@event.Userid, GameplayStatKind.BombDefused, "bomb_defused");
        return HookResult.Continue;
    }

    private HookResult OnHostageRescued(EventHostageRescued @event, GameEventInfo _)
    {
        RecordGameplayStat(@event.Userid, GameplayStatKind.HostageRescued, "hostage_rescued");
        return HookResult.Continue;
    }

    private HookResult OnHostageKilled(EventHostageKilled @event, GameEventInfo _)
    {
        RecordGameplayStat(@event.Userid, GameplayStatKind.HostageKilled, "hostage_killed");
        return HookResult.Continue;
    }

    private HookResult OnRoundMvp(EventRoundMvp @event, GameEventInfo _)
    {
        RecordGameplayStat(@event.Userid, GameplayStatKind.Mvp, "round_mvp");
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo _)
    {
        RecordLiveRankRound(@event.Winner);
        var gameplay = _gameplayStats;
        var players = _players;
        if (gameplay is null || players is null || !GameplayStatsAllowed())
            return HookResult.Continue;

        try
        {
            var winner = @event.Winner switch
            {
                2 => PlayerTeam.Terrorist,
                3 => PlayerTeam.CounterTerrorist,
                _ => PlayerTeam.Unknown,
            };
            var now = DateTimeOffset.UtcNow;
            var map = CombatDetailKey(Server.MapName, "unknown_map", 128);
            foreach (var statistic in GameplayStatEventFactory.Round(
                         _combatServerInstance, map, CombatMapEpoch(), Server.TickCount,
                         now, players.OnlinePlayers, winner))
            {
                Observe(gameplay.RecordAsync(statistic).AsTask(), "gameplay_round");
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Could not record gameplay round statistics.");
        }

        return HookResult.Continue;
    }

    private HookResult OnMatchEnd(EventCsWinPanelMatch @event, GameEventInfo _)
    {
        RecordLiveRankMatch();
        var gameplay = _gameplayStats;
        if (gameplay is null || !GameplayStatsAllowed())
            return HookResult.Continue;

        try
        {
            var participants = Utilities.GetPlayers()
                .Select(controller => (Controller: controller, Player: CombatPlayer(controller)))
                .Where(value => value.Player is not null)
                .Select(value => new GameplayMatchParticipant(
                    value.Player!.Id, value.Player.Team, value.Controller.Score))
                .ToArray();

            var winningTeam = PlayerTeam.Unknown;
            if (!gameplay.Configuration.FreeForAll)
            {
                var ctScore = 0;
                var terroristScore = 0;
                foreach (var team in Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager"))
                {
                    if (string.Equals(team.Teamname, "CT", StringComparison.OrdinalIgnoreCase))
                        ctScore = team.Score;
                    else if (string.Equals(
                                 team.Teamname, "TERRORIST", StringComparison.OrdinalIgnoreCase))
                        terroristScore = team.Score;
                }

                winningTeam = ctScore > terroristScore
                    ? PlayerTeam.CounterTerrorist
                    : terroristScore > ctScore
                        ? PlayerTeam.Terrorist
                        : PlayerTeam.Unknown;
            }

            var now = DateTimeOffset.UtcNow;
            var map = CombatDetailKey(Server.MapName, "unknown_map", 128);
            foreach (var statistic in GameplayStatEventFactory.Match(
                         _combatServerInstance, map, CombatMapEpoch(), Server.TickCount,
                         now, participants, gameplay.Configuration.FreeForAll, winningTeam))
            {
                Observe(gameplay.RecordAsync(statistic).AsTask(), "gameplay_match");
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Could not record gameplay match statistics.");
        }

        return HookResult.Continue;
    }

    private void RecordGameplayStat(
        CCSPlayerController? controller,
        GameplayStatKind kind,
        string signature)
    {
        RecordLiveRankGameplay(controller, kind, signature);
        var gameplay = _gameplayStats;
        if (gameplay is null || !GameplayStatsAllowed()) return;

        try
        {
            var player = CombatPlayer(controller);
            if (player is null) return;
            var map = CombatDetailKey(Server.MapName, "unknown_map", 128);
            var statistic = GameplayStatEventFactory.Player(
                _combatServerInstance, map, CombatMapEpoch(), Server.TickCount,
                DateTimeOffset.UtcNow, player.Id, kind, signature);
            Observe(gameplay.RecordAsync(statistic).AsTask(), $"gameplay_{kind}");
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Could not record gameplay statistic {Statistic}.", kind);
        }
    }

    private RankLiveContext LiveRankContext(Guid eventId, DateTimeOffset at)
    {
        var warmup = true;
        try
        {
            warmup = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault()?.GameRules?.WarmupPeriod ?? true;
        }
        catch (Exception exception) { Logger.LogDebug(exception, "Could not inspect warmup state for ranks."); }
        var humans = _players?.OnlinePlayers.Count(player => player.IsConnected
            && player.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist) ?? 0;
        return new(eventId, at, warmup, humans,
            FormattableString.Invariant($"{Server.MapName}|{CombatMapEpoch()}|{Interlocked.Read(ref _rankRoundGeneration)}"));
    }

    private RankParticipant? LiveRankParticipant(CCSPlayerController? controller)
    {
        if (controller is not { IsValid: true, IsHLTV: false }) return null;
        if (!controller.IsBot)
        {
            var player = CombatPlayer(controller);
            return player is null ? null : new(player, player.Team, false);
        }
        var team = controller.TeamNum switch
        {
            2 => PlayerTeam.Terrorist,
            3 => PlayerTeam.CounterTerrorist,
            _ => PlayerTeam.Unknown,
        };
        return new(null, team, true);
    }

    private void RecordLiveRankDeath(EventPlayerDeath value)
    {
        var scoring = _liveRankScoring;
        if (scoring is null) return;
        try
        {
            var victim = LiveRankParticipant(value.Userid);
            var attacker = LiveRankParticipant(value.Attacker);
            if (victim is null || value.Attacker is not null && attacker is null) return;
            var identityPlayer = victim.Player?.Id ?? attacker?.Player?.Id;
            if (identityPlayer is null) return;
            var eventId = CombatEventIdentity.CreateDetail(_combatServerInstance, Server.MapName,
                CombatMapEpoch(), Server.TickCount, "rank_death", identityPlayer,
                attacker?.Player?.Id, FormattableString.Invariant($"victim:{value.Userid?.Slot ?? -1}"));
            var specials = new List<GameplayStatKind>();
            if (value.Headshot) specials.Add(GameplayStatKind.HeadshotKill);
            if (value.Noscope) specials.Add(GameplayStatKind.NoScopeKill);
            if (value.Penetrated > 0) specials.Add(GameplayStatKind.PenetratedKill);
            if (value.Thrusmoke) specials.Add(GameplayStatKind.ThroughSmokeKill);
            if (value.Attackerblind) specials.Add(GameplayStatKind.FlashedKill);
            if (value.Dominated > 0) specials.Add(GameplayStatKind.DominatedKill);
            if (value.Revenge > 0) specials.Add(GameplayStatKind.RevengeKill);
            var at = DateTimeOffset.UtcNow;
            var distance = float.IsFinite(value.Distance) ? (decimal)Math.Clamp(value.Distance, 0f, 10000f) : 0m;
            var input = new RankDeathInput(LiveRankContext(eventId, at), victim, attacker,
                CombatPlayer(value.Assister), CombatDetailKey(value.Weapon, "world", 64),
                specials.AsReadOnly(), value.Assistedflash, distance);
            Observe(scoring.RecordDeathAsync(input).AsTask(), "rank_death");
        }
        catch (Exception exception) { Logger.LogError(exception, "Could not record live rank death."); }
    }

    private void RecordLiveRankGameplay(CCSPlayerController? controller, GameplayStatKind kind, string signature)
    {
        var scoring = _liveRankScoring;
        if (scoring is null || LiveRankPolicy.IsKillSpecial(kind) || kind == GameplayStatKind.FlashAssist) return;
        try
        {
            var player = CombatPlayer(controller);
            if (player is null) return;
            var at = DateTimeOffset.UtcNow;
            var statistic = GameplayStatEventFactory.Player(_combatServerInstance,
                CombatDetailKey(Server.MapName, "unknown_map", 128), CombatMapEpoch(), Server.TickCount,
                at, player.Id, kind, signature);
            Observe(scoring.RecordGameplayAsync(statistic, LiveRankContext(statistic.EventId, at), player).AsTask(), "rank_gameplay");
        }
        catch (Exception exception) { Logger.LogError(exception, "Could not record live rank gameplay event."); }
    }

    private void RecordLiveRankRound(int winningTeam)
    {
        var scoring = _liveRankScoring;
        var players = _players;
        if (scoring is null || players is null) return;
        try
        {
            var winner = winningTeam == 2 ? PlayerTeam.Terrorist
                : winningTeam == 3 ? PlayerTeam.CounterTerrorist : PlayerTeam.Unknown;
            var snapshots = players.OnlinePlayers.ToArray();
            var at = DateTimeOffset.UtcNow;
            foreach (var statistic in GameplayStatEventFactory.Round(_combatServerInstance,
                CombatDetailKey(Server.MapName, "unknown_map", 128), CombatMapEpoch(), Server.TickCount, at, snapshots, winner))
            {
                var player = snapshots.First(value => value.Id == statistic.PlayerId);
                Observe(scoring.RecordGameplayAsync(statistic, LiveRankContext(statistic.EventId, at), player).AsTask(), "rank_round");
            }
        }
        catch (Exception exception) { Logger.LogError(exception, "Could not record live rank round."); }
    }

    private void RecordLiveRankMatch()
    {
        var scoring = _liveRankScoring;
        if (scoring is null) return;
        try
        {
            var snapshots = Utilities.GetPlayers().Select(controller => (Controller: controller, Player: CombatPlayer(controller)))
                .Where(value => value.Player is not null).ToArray();
            var participants = snapshots.Select(value => new GameplayMatchParticipant(value.Player!.Id,
                value.Player.Team, value.Controller.Score)).ToArray();
            var teams = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager").ToArray();
            var ct = teams.FirstOrDefault(team => team.Teamname == "CT")?.Score ?? 0;
            var t = teams.FirstOrDefault(team => team.Teamname == "TERRORIST")?.Score ?? 0;
            var winner = ct > t ? PlayerTeam.CounterTerrorist : t > ct ? PlayerTeam.Terrorist : PlayerTeam.Unknown;
            var at = DateTimeOffset.UtcNow;
            foreach (var statistic in GameplayStatEventFactory.Match(_combatServerInstance,
                CombatDetailKey(Server.MapName, "unknown_map", 128), CombatMapEpoch(), Server.TickCount,
                at, participants, scoring.Policy.FreeForAll, winner))
            {
                var player = snapshots.First(value => value.Player!.Id == statistic.PlayerId).Player!;
                Observe(scoring.RecordGameplayAsync(statistic, LiveRankContext(statistic.EventId, at), player).AsTask(), "rank_match");
            }
        }
        catch (Exception exception) { Logger.LogError(exception, "Could not record live rank match."); }
    }

    private bool GameplayStatsAllowed()
    {
        var gameplay = _gameplayStats;
        if (gameplay is null) return true;

        var warmup = false;
        try
        {
            warmup = Utilities
                .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault()?.GameRules?.WarmupPeriod ?? false;
        }
        catch (Exception exception)
        {
            Logger.LogDebug(exception, "Could not inspect warmup state for gameplay statistics.");
        }

        return GameplayStatsEligibility.IsAllowed(
            gameplay.Configuration,
            warmup,
            _players?.OnlinePlayers.Count ?? 0);
    }

    private static long CombatMapEpoch()
        => checked((long)Math.Round(Server.EngineTime - Server.CurrentTime));

    private static string CombatDetailKey(string? value, string fallback, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var safe = new string(normalized
            .Where(character => !char.IsControl(character))
            .Take(maxLength)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
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
