using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Placeholders;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Abstractions.Settings;
using AnoCore.Abstractions.Targeting;
using AnoCore.Abstractions.Voting;
using AnoCore.Abstractions.Warnings;
using AnoCore.Runtime.Auditing;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Menus;
using AnoCore.Runtime.Moderation;
using AnoCore.Runtime.Modules;
using AnoCore.Runtime.Permissions;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using AnoCore.Runtime.Placeholders;
using AnoCore.Runtime.Players;
using AnoCore.Runtime.Settings;
using AnoCore.Runtime.Targeting;
using AnoCore.Runtime.Voting;
using AnoCore.Runtime.Warnings;

namespace AnoCore.Runtime.Composition;

public sealed class RuntimeServices : IServiceProvider, IDisposable
{
    private static readonly ModuleId CoreModule = new("core");
    private readonly Dictionary<Type, object> _services = [];
    private readonly List<IDisposable> _registrations = [];
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
        Settings = new PlayerSettingsService(data, null, events);
        ToggleCatalog = new PlayerToggleCatalog();
        WarningRepository = new MySqlWarningRepository(database);
        Warnings = new WarningService(WarningRepository);
        AdminAuditRepository = new MySqlAdminAuditRepository(database);
        AdminAudit = new AdminAuditService(AdminAuditRepository);
        ModerationRepository = new MySqlModerationRepository(database);
        Moderation = new ModerationService(ModerationRepository);
        Players = players;
        TargetResolver = new PlayerTargetResolver(players);
        TargetAuthorization = new TargetAuthorizationService(players, Authorization);
        Modules = new ModuleHost(new ModuleContext(this));

        Add<IDatabase>(database);
        Add<IConfigStore>(configuration);
        Add<IAnoEventBus>(events);
        Add<IPlayerRegistry>(players);
        Add<IPlayerRepository>(Profiles);
        Add<IModuleDataStore>(data);
        Add<IAuthorizationStore>(authorizationStore);
        Add<IAuthorizationService>(Authorization);
        Add<IPermissionEvaluator>(Authorization);
        Add<IAnoCommandRegistry>(Commands);
        Add<IMenuService>(Menus);
        Add<IPlayerSettingsService>(Settings);
        Add<IPlayerToggleCatalog>(ToggleCatalog);
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

    public PlayerSettingsService Settings { get; }

    public PlayerToggleCatalog ToggleCatalog { get; }

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
            [new CoreSchemaMigration001(), new ModerationSchemaMigration002(), new AdminAuditSchemaMigration003(), new WarningSchemaMigration004()])
            .EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var runtime = new RuntimeServices(database, configuration, events, players);
        try
        {
            await runtime.Authorization.ReloadAsync(cancellationToken).ConfigureAwait(false);
            runtime.Subscribe(events);
            foreach (var player in players.OnlinePlayers)
            {
                await runtime.SaveAsync(player, cancellationToken).ConfigureAwait(false);
            }

            runtime.RegisterCommands();
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

        for (var index = _registrations.Count - 1; index >= 0; index--)
        {
            _registrations[index].Dispose();
        }

        _registrations.Clear();
    }

    private void Add<T>(T service)
        where T : class
        => _services.Add(typeof(T), service);

    private void Subscribe(AnoEventBus events)
    {
        _registrations.Add(events.Subscribe<PlayerConnectedEvent>(
            (value, token) => SaveAsync(value.Player, token)));
        _registrations.Add(events.Subscribe<PlayerReconnectedEvent>(
            (value, token) => SaveAsync(value.Current, token)));
        _registrations.Add(events.Subscribe<PlayerDisconnectedEvent>(
            (value, token) =>
            {
                Menus.Close(value.Player.Id);
                return SaveAsync(value.Player, token);
            }));
        _registrations.Add(events.Subscribe<PlayerUpdatedEvent>(
            (value, token) => value.Previous.Name != value.Current.Name
                ? SaveAsync(value.Current, token)
                : ValueTask.CompletedTask));
    }

    private ValueTask SaveAsync(PlayerSnapshot player, CancellationToken cancellationToken)
        => Profiles.UpsertAsync(
            new PlayerProfile(player.Id, player.Name, player.ConnectedAtUtc, player.LastUpdatedAtUtc),
            cancellationToken);

    private void RegisterCommands()
    {
        _registrations.Add(new PlayerToggleCommandModule(
            Commands, Players, ToggleCatalog, Settings));
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
    }
}
