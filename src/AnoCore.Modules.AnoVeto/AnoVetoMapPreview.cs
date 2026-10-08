using System.Security.Cryptography;
using System.Text;
using AnoCore.Abstractions.Maps;

namespace AnoCore.Modules.AnoVeto;

/// <summary>Stable identity shared with the offline UI asset preparer; never a remote URL.</summary>
public static class AnoVetoMapPreview
{
    public static string GetClass(MapDefinition map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.WorkshopId is { } workshopId
            ? $"ano_preview_w_{workshopId}"
            : $"ano_preview_m_{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(map.MapId)))[..16]}";
    }
}
