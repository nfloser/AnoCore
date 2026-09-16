using System.Text.RegularExpressions;

namespace AnoCore.Abstractions.Maps;

public sealed record MapDefinition
{
    private static readonly Regex MapIdPattern = new("^[a-zA-Z0-9_./-]{1,128}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public MapDefinition(string displayName, string mapId, ulong? workshopId = null)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A map display name is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(mapId) || !MapIdPattern.IsMatch(mapId.Trim())) throw new ArgumentException("A safe map ID is required.", nameof(mapId));
        if (workshopId == 0) throw new ArgumentOutOfRangeException(nameof(workshopId), "Workshop ID must be greater than zero.");
        DisplayName = displayName.Trim(); MapId = mapId.Trim(); WorkshopId = workshopId;
    }
    public string DisplayName { get; }
    public string MapId { get; }
    public ulong? WorkshopId { get; }
}
