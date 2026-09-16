namespace AnoCore.Abstractions.Maps;

public interface IMapChanger { ValueTask ChangeMapAsync(MapDefinition map, CancellationToken cancellationToken = default); }
