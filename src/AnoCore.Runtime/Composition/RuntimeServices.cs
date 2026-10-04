using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Management;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Placeholders;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Abstractions.Settings;
using AnoCore.Abstractions.Stats;
using AnoCore.Abstractions.Targeting;
using AnoCore.Abstractions.Voting;
using AnoCore.Abstractions.Warnings;
using AnoCore.Runtime.Auditing;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Management;
using AnoCore.Runtime.Menus;
using AnoCore.Runtime.Messaging;
using AnoCore.Runtime.Moderation;
using AnoCore.Runtime.Modules;
using AnoCore.Runtime.Permissions;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using AnoCore.Runtime.Placeholders;
using AnoCore.Runtime.Players;
using AnoCore.Runtime.Settings;
using AnoCore.Runtime.Stats;
using AnoCore.Runtime.Targeting;
using AnoCore.Runtime.Voting;
using AnoCore.Runtime.Warnings;

namespace AnoCore.Runtime.Composition;

public sealed class RuntimeServices : IServiceProvider, IDisposable
{
    private static readonly ModuleId CoreModule = new("core");
    private readonly Dictionary<Type, object> _services = [];
    private readonly List<IDisposable> _registrations = [];
    private readonly PlayerProfileLifecycle _profileLifecycle;
    private int _disposed;

    private RuntimeServices(
        IDatabase database,
        IConfigStore configuration,
        AnoEventBus events,
        PlayerRegistry players)
    {
        Profiles = new MySqlPlayerRepository(database);
        var data = new MySqlModuleDataStore(database);
        var authorizationStore = new ModuleDataAuthorizationStore(data);
        Authorization = new AuthorizationService(authorizationStore);
        Commands = new CommandRegistry(Authorization);
        Menus = new MenuService();
        Messages = new MessageService();
        Settings = new PlayerSettingsService(data, null, events);
        ToggleCatalog = new PlayerToggleCatalog();
        ConfigReloads = new ConfigReloadRegistry();
        Playtime = new MySqlPlaytimeRepository(database);
        Combat = new MySqlCombatRepository(database);
        GameplayStats = new MySqlGameplayStatRepository(database);
        RankAdjustments = new MySqlRankAdjustmentRepository(database);
        RankAdjustmentAdministration = new MySqlRankAdjustmentAdministrationService(database);
        StatisticsResetAdministration = new MySqlStatisticsResetAdministrationService(database);
        WarningRepository = new MySqlWarningRepository(database);
        Warnings = new WarningService(WarningRepository);
        AdminAuditRepository = new MySqlAdminAuditRepository(database);
        AdminAudit = new AdminAuditService(AdminAuditRepository);
        ModerationRepository = new MySqlModerationRepository(database);
        Moderation = new ModerationService(ModerationRepository);
        Players = players;
        _profileLifecycle = new PlayerProfileLifecycle(Profiles, Players, events);
        TargetResolver = new PlayerTargetResolver(players);
        TargetAuthorization = new TargetAuthorizationService(players, Authorization);
        Modules = new ModuleHost(new ModuleContext(this));
        ManagementCapabilities = new ManagementCapabilityRegistry();
        ManagementStatus = new RuntimeManagementStatusProvider(database, players, Modules);

        Add<IDatabase>(database);
        Add<IConfigStore>(configuration);
        Add<IConfigReloadRegistry>(ConfigReloads);
        if (configuration is IVersionedConfigStore versionedConfiguration)
            Add<IVersionedConfigStore>(versionedConfiguration);
        Add<IAnoEventBus>(events);
        Add<IPlayerRegistry>(players);
        Add<IPlayerRepository>(Profiles);
        Add<IModuleDataStore>(data);
        Add<IModuleDataPrefixStore>(data);
        Add<IModuleDataBatchStore>(data);
        Add<IAuthorizationStore>(authorizationStore);
        Add<IAuthorizationService>(Authorization);
        Add<IPermissionEvaluator>(Authorization);
        Add<IAnoCommandRegistry>(Commands);
        Add<IMenuService>(Menus);
        Add<IMessageService>(Messages);
        Add<IManagementCapabilityRegistry>(ManagementCapabilities);
        Add<IManagementStatusProvider>(ManagementStatus);
        Add<IPlayerSettingsService>(Settings);
        Add<IPlayerSettingsResetService>(Settings);
        Add<IPlayerSettingsBatchService>(Settings);
        Add<IPlayerToggleCatalog>(ToggleCatalog);
        Add<IPlaytimeRepository>(Playtime);
        Add<IPlaytimeStateRepository>(Playtime);
        Add<ICombatRepository>(Combat);
        Add<ICombatDetailRepository>(Combat);
        Add<IGameplayStatRepository>(GameplayStats);
        Add<IRankAdjustmentRepository>(RankAdjustments);
        Add<IRankAdjustmentAdministrationService>(RankAdjustmentAdministration);
        Add<IStatisticsResetAdministrationService>(StatisticsResetAdministration);
        Add<IWarningRepository>(WarningRepository);
        Add<IWarningService>(Warnings);
        Add<IAdminAuditRepository>(AdminAuditRepository);
        Add<IAdminAuditService>(AdminAudit);
        Add<IModerationRepository>(ModerationRepository);
        Add<IModerationService>(Moderation);
        Add<IModerationSnapshotProvider>(Moderation);
        Add<IPlayerTargetResolver>(TargetResolver);
        Add<ITargetAuthorizationService>(TargetAuthorization);
        Add<IPlaceholderRegistry>(new PlaceholderRegistry());
        Add<IVoteService>(new VoteService(Authorization));
    }

    public IPlayerRepository Profiles { get; }

    public IPlayerRegistry Players { get; }

    public AuthorizationService Authorization { get; }

    public CommandRegistry Commands { get; }

    public MenuService Menus { get; }

    public MessageService Messages { get; }

    public ManagementCapabilityRegistry ManagementCapabilities { get; }

    public RuntimeManagementStatusProvider ManagementStatus { get; }

    public PlayerSettingsService Settings { get; }

    public PlayerToggleCatalog ToggleCatalog { get; }

    public ConfigReloadRegistry ConfigReloads { get; }

    public MySqlPlaytimeRepository Playtime { get; }

    public MySqlCombatRepository Combat { get; }

    public MySqlGameplayStatRepository GameplayStats { get; }

    public MySqlRankAdjustmentRepository RankAdjustments { get; }

    public MySqlRankAdjustmentAdministrationService RankAdjustmentAdministration { get; }

    public MySqlStatisticsResetAdministrationService StatisticsResetAdministration { get; }

    public MySqlWarningRepository WarningRepository { get; }

    public WarningService Warnings { get; }

    public MySqlAdminAuditRepository AdminAuditRepository { get; }

    public AdminAuditService AdminAudit { get; }

    public MySqlModerationRepository ModerationRepository { get; }

    public ModerationService Moderation { get; }

    public PlayerTargetResolver TargetResolver { get; }

    public TargetAuthorizationService TargetAuthorization { get; }

    public ModuleHost Modules { get; }

    public static async Task<RuntimeServices> CreateAsync(
        IDatabase database,
        IConfigStore configuration,
        AnoEventBus events,
        PlayerRegistry players,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(players);
        await new DatabaseStartupProbe(
            database,
            [new CoreSchemaMigration001(), new ModerationSchemaMigration002(), new AdminAuditSchemaMigration003(), new WarningSchemaMigration004(), new PlaytimeSchemaMigration005(), new CombatSchemaMigration006(), new RankAdjustmentSchemaMigration007(), new PlaytimeStateSchemaMigration008(), new CombatDetailSchemaMigration009(), new GameplayStatSchemaMigration010(),
             new StatisticsResetSchemaMigration011()])
            .EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var runtime = new RuntimeServices(database, configuration, events, players);
        try
        {
            await runtime.Authorization.ReloadAsync(cancellationToken).ConfigureAwait(false);
            runtime.Subscribe(events);
            await runtime._profileLifecycle.StartAsync(cancellationToken).ConfigureAwait(false);

            runtime.RegisterCommands(events);
            cancellationToken.ThrowIfCancellationRequested();
            return runtime;
        }
        catch
        {
            runtime.Dispose();
            throw;
        }
    }

    public object? GetService(Type serviceType)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(serviceType);
        return _services.GetValueOrDefault(serviceType);
    }

    public async Task SaveOnlinePlayersAsync(CancellationToken cancellationToken = default)
    {
        foreach (var player in Players.OnlinePlayers)
        {
            await Profiles.UpsertAsync(
                new PlayerProfile(player.Id, player.Name, player.ConnectedAtUtc, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _profileLifecycle.Dispose();

        for (var index = _registrations.Count - 1; index >= 0; index--)
        {
            _registrations[index].Dispose();
        }

        _registrations.Clear();
        Messages.Dispose();
    }

    private void Add<T>(T service)
        where T : class
        => _services.Add(typeof(T), service);

    private void Subscribe(AnoEventBus events)
    {
        _registrations.Add(events.Subscribe<PlayerDisconnectedEvent>(
            (value, _) =>
            {
                Menus.Close(value.Player.Id);
                return ValueTask.CompletedTask;
            }));
    }

    private void RegisterCommands(AnoEventBus events)
    {
        _registrations.Add(new PlayerToggleCommandModule(
            Commands, Players, ToggleCatalog, Settings, Menus, events));
        _registrations.Add(Commands.Register(
            CoreModule,
            new CommandDescriptor("anocommands", "List registered AnoCore commands"),
            _ => ValueTask.FromResult(CommandResult.Ok(string.Join(
                " | ", Commands.GetCommands().Select(command => $"!{command.Name}: {command.Description}"))))));
        _registrations.Add(Commands.Register(
            CoreModule,
            new CommandDescriptor(
                "anoreloadauth", "Reload persisted AnoCore roles and permissions", new PermissionId("ano.core.reload")),
            async context =>
            {
                await Authorization.ReloadAsync(context.CancellationToken).ConfigureAwait(false);
                return CommandResult.Ok("[ANO] Authorization reloaded.");
            }));
        _registrations.Add(new ConfigReloadCommandController(Commands, ConfigReloads));
    }
}
