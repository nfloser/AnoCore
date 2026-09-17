using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Maps;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Runtime.Maps;

namespace AnoCore.Modules.AnoVeto;

public sealed class AnoVetoModuleRuntime : IDisposable
{
    private AnoVetoCommandController? _controller;

    private AnoVetoModuleRuntime(
        AnoVetoCoordinator coordinator,
        AnoVetoCommandController controller)
    {
        Coordinator = coordinator;
        _controller = controller;
    }

    public AnoVetoCoordinator Coordinator { get; }

    public static async ValueTask<AnoVetoModuleRuntime?> CreateAsync(
        IConfigStore configuration,
        IAnoCommandRegistry commands,
        ICustomHudService hud,
        IPlayerRegistry players,
        IVoteService votes,
        IMapChanger mapChanger,
        TimeProvider? timeProvider = null,
        IAnoVetoRandomSource? random = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(hud);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(votes);
        ArgumentNullException.ThrowIfNull(mapChanger);

        var settings = await configuration.LoadAsync(
            "anoveto",
            () => new AnoVetoConfiguration(),
            AnoVetoConfiguration.Validate,
            cancellationToken).ConfigureAwait(false);
        if (!settings.Enabled)
        {
            return null;
        }

        var catalog = await new MapCatalogLoader(configuration)
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        var coordinator = new AnoVetoCoordinator(
            catalog,
            votes,
            mapChanger,
            random ?? new AnoVetoRandomSource(),
            settings.ToOptions());
        var controller = new AnoVetoCommandController(
            commands,
            hud,
            players,
            coordinator,
            timeProvider);
        return new AnoVetoModuleRuntime(coordinator, controller);
    }

    public ValueTask<AnoVetoOperationResult?> ExpireAsync(CancellationToken cancellationToken = default)
    {
        var controller = Volatile.Read(ref _controller);
        return controller is null
            ? ValueTask.FromResult<AnoVetoOperationResult?>(null)
            : controller.ExpireAsync(cancellationToken);
    }

    public void Dispose()
        => Interlocked.Exchange(ref _controller, null)?.Dispose();
}
