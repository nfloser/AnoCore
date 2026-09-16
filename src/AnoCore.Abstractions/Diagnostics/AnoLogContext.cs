using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Diagnostics;

public sealed record AnoLogContext(
    ModuleId? Module,
    PlayerId? Player,
    string? Operation)
{
    public IReadOnlyDictionary<string, object> ToProperties()
    {
        var properties = new Dictionary<string, object>(StringComparer.Ordinal);

        if (Module is not null)
        {
            properties["AnoModule"] = $"ano.{Module.Value}";
        }

        if (Player is not null)
        {
            properties["SteamId"] = Player.SteamId64;
        }

        if (!string.IsNullOrWhiteSpace(Operation))
        {
            properties["AnoOperation"] = Operation.Trim();
        }

        return properties;
    }
}
