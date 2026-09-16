using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Maps;
namespace AnoCore.Runtime.Maps;

public sealed record MapCatalogConfiguration(IReadOnlyList<MapDefinition> Maps) { public static MapCatalogConfiguration Empty { get; } = new([]); }
public sealed class MapCatalogLoader(IConfigStore configStore)
{
    private readonly IConfigStore _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
    public async ValueTask<MapCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await _configStore.LoadAsync("maps", () => MapCatalogConfiguration.Empty, Validate, cancellationToken).ConfigureAwait(false);
        return new MapCatalog(configuration.Maps);
    }
    private static IReadOnlyCollection<string> Validate(MapCatalogConfiguration configuration)
    {
        try { _ = new MapCatalog(configuration.Maps); return []; }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException) { return [exception.Message]; }
    }
}
