namespace AnoCore.Abstractions.Maps;

public interface IMapCatalog
{
    IReadOnlyList<MapDefinition> All { get; }
    bool TryGetByName(string displayName, out MapDefinition? map);
    bool TryGetByMapId(string mapId, out MapDefinition? map);
    bool TryGetByWorkshopId(ulong workshopId, out MapDefinition? map);
}
