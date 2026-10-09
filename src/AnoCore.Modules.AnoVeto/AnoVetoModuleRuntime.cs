using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Maps;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Runtime.Maps;

namespace AnoCore.Modules.AnoVeto;

public sealed class AnoVetoModuleRuntime : IDisposable
{
    private static readonly ModuleId Owner = new("ano.veto");

    private AnoVetoCommandController? _controller;
    private IConfigReloadRegistration<MapCatalog>? _mapReload;
    private IConfigReloadRegistration<AnoVetoConfiguration>? _settingsReload;

    private AnoVetoModuleRuntime(
        AnoVetoCoordinator coordinator,
        AnoVetoCommandController controller,
        IConfigReloadRegistration<AnoVetoConfiguration>? settingsReload = null,
        IConfigReloadRegistration<MapCatalog>? mapReload = null)
    {
        Coordinator = coordinator;
        _controller = controller;
        _settingsReload = settingsReload;
        _mapReload = mapReload;
    }

    public AnoVetoCoordinator Coordinator { get; }

    public bool IsActive => Volatile.Read(ref _controller) is not null;

    public static ValueTask<AnoVetoModuleRuntime?> CreateAsync(
        IConfigStore configuration,
        IAnoCommandRegistry commands,
        ICustomHudService hud,
        IPlayerRegistry players,
        IVoteService votes,
        IMapChanger mapChanger,
        TimeProvider? timeProvider = null,
        IAnoVetoRandomSource? random = null,
        CancellationToken cancellationToken = default)
        => CreateCoreAsync(
            configuration,
            commands,
            hud,
            players,
            votes,
            mapChanger,
            timeProvider,
            random,
            cancellationToken,
            reloads: null);

    public static ValueTask<AnoVetoModuleRuntime?> CreateAsync(
        IConfigStore configuration,
        IAnoCommandRegistry commands,
        ICustomHudService hud,
        IPlayerRegistry players,
        IVoteService votes,
        IMapChanger mapChanger,
        IConfigReloadRegistry reloads,
        TimeProvider? timeProvider = null,
        IAnoVetoRandomSource? random = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reloads);
        return CreateCoreAsync(
            configuration,
            commands,
            hud,
            players,
            votes,
            mapChanger,
            timeProvider,
            random,
            cancellationToken,
            reloads);
    }

    public ValueTask<AnoVetoOperationResult?> ExpireAsync(CancellationToken cancellationToken = default)
    {
        var controller = Volatile.Read(ref _controller);
        return controller is null
            ? ValueTask.FromResult<AnoVetoOperationResult?>(null)
            : controller.ExpireAsync(cancellationToken);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _controller, null)?.Dispose();
        Interlocked.Exchange(ref _mapReload, null)?.Dispose();
        Interlocked.Exchange(ref _settingsReload, null)?.Dispose();
    }

    private static async ValueTask<AnoVetoModuleRuntime?> CreateCoreAsync(
        IConfigStore configuration,
        IAnoCommandRegistry commands,
        ICustomHudService hud,
        IPlayerRegistry players,
        IVoteService votes,
        IMapChanger mapChanger,
        TimeProvider? timeProvider,
        IAnoVetoRandomSource? random,
        CancellationToken cancellationToken,
        IConfigReloadRegistry? reloads)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(hud);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(votes);
        ArgumentNullException.ThrowIfNull(mapChanger);

        var settings = await LoadSettingsAsync(configuration, cancellationToken).ConfigureAwait(false);
        if (!settings.Enabled)
        {
            return null;
        }

        var catalogLoader = new MapCatalogLoader(configuration);
        var catalog = await catalogLoader.LoadAsync(cancellationToken).ConfigureAwait(false);
        var randomSource = random ?? new AnoVetoRandomSource();

        if (reloads is null)
        {
            var fixedCoordinator = new AnoVetoCoordinator(
                catalog,
                votes,
                mapChanger,
                randomSource,
                settings.ToOptions());
            var fixedController = new AnoVetoCommandController(
                commands,
                hud,
                players,
                fixedCoordinator,
                timeProvider);
            return new AnoVetoModuleRuntime(fixedCoordinator, fixedController);
        }

        IConfigReloadRegistration<AnoVetoConfiguration>? settingsReload = null;
        IConfigReloadRegistration<MapCatalog>? mapReload = null;
        AnoVetoCommandController? controller = null;

        try
        {
            settingsReload = reloads.Register(
                Owner,
                "anoveto",
                settings,
                token => LoadSettingsAsync(configuration, token),
                ValidateLiveSettings);
            var activeSettings = settingsReload;

            mapReload = reloads.Register(
                Owner,
                "maps",
                catalog,
                token => catalogLoader.LoadAsync(token));
            var activeMaps = mapReload;

            var coordinator = new AnoVetoCoordinator(
                () => activeMaps.Current,
                votes,
                mapChanger,
                randomSource,
                () => activeSettings.Current.ToOptions());
            controller = new AnoVetoCommandController(
                commands,
                hud,
                players,
                coordinator,
                timeProvider);

            return new AnoVetoModuleRuntime(
                coordinator,
                controller,
                settingsReload,
                mapReload);
        }
        catch
        {
            controller?.Dispose();
            mapReload?.Dispose();
            settingsReload?.Dispose();
            throw;
        }
    }

    private static ValueTask<AnoVetoConfiguration> LoadSettingsAsync(
        IConfigStore configuration,
        CancellationToken cancellationToken)
        => configuration.LoadAsync(
            "anoveto",
            () => new AnoVetoConfiguration(),
            AnoVetoConfiguration.Validate,
            cancellationToken);

    private static IReadOnlyCollection<string> ValidateLiveSettings(
        AnoVetoConfiguration configuration)
    {
        var errors = AnoVetoConfiguration.Validate(configuration).ToList();
        if (!configuration.Enabled)
        {
            errors.Add("Enabled is a startup setting and cannot be disabled by live reload.");
        }

        return errors;
    }
}
