using AnoCore.Abstractions.Maps;
namespace AnoCore.Runtime.Maps;

public sealed class MapCatalog : IMapCatalog
{
    private readonly Dictionary<string, MapDefinition> _byName;
    private readonly Dictionary<string, MapDefinition> _byMapId;
    private readonly Dictionary<ulong, MapDefinition> _byWorkshopId;
    public MapCatalog(IEnumerable<MapDefinition> maps)
    {
        ArgumentNullException.ThrowIfNull(maps); var entries = maps.ToArray();
        _byName = new(StringComparer.OrdinalIgnoreCase); _byMapId = new(StringComparer.OrdinalIgnoreCase); _byWorkshopId = [];
        foreach (var map in entries)
        {
            if (!_byName.TryAdd(map.DisplayName, map)) throw new ArgumentException($"Duplicate map display name '{map.DisplayName}'.", nameof(maps));
            if (!_byMapId.TryAdd(map.MapId, map)) throw new ArgumentException($"Duplicate map ID '{map.MapId}'.", nameof(maps));
            if (map.WorkshopId is { } workshopId && !_byWorkshopId.TryAdd(workshopId, map)) throw new ArgumentException($"Duplicate workshop ID '{workshopId}'.", nameof(maps));
        }
        All = entries.OrderBy(map => map.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public IReadOnlyList<MapDefinition> All { get; }
    public bool TryGetByName(string displayName, out MapDefinition? map) => _byName.TryGetValue(displayName?.Trim() ?? string.Empty, out map);
    public bool TryGetByMapId(string mapId, out MapDefinition? map) => _byMapId.TryGetValue(mapId?.Trim() ?? string.Empty, out map);
    public bool TryGetByWorkshopId(ulong workshopId, out MapDefinition? map) => _byWorkshopId.TryGetValue(workshopId, out map);
}
