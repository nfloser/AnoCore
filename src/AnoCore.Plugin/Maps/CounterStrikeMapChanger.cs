using AnoCore.Abstractions.Maps;
using CounterStrikeSharp.API;
namespace AnoCore.Plugin.Maps;

public sealed class CounterStrikeMapChanger : IMapChanger
{
    public ValueTask ChangeMapAsync(MapDefinition map, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map); cancellationToken.ThrowIfCancellationRequested();
        Server.NextFrame(() => { if (map.WorkshopId is { } workshopId) Server.ExecuteCommand($"host_workshop_map {workshopId}"); else Server.ExecuteCommand($"changelevel \"{map.MapId}\""); });
        return ValueTask.CompletedTask;
    }
}
