using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

// Values mirror the native chat protocol exposed by the supported server API.
public static class ChatColorPalette
{
    public const char Default = '\x01';

    private static readonly IReadOnlyDictionary<string, char> Named =
        new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
        {
            ["Default"] = Default,
            ["White"] = '\x01',
            ["DarkRed"] = '\x02',
            ["LightPurple"] = '\x03',
            ["Green"] = '\x04',
            ["Olive"] = '\x05',
            ["Lime"] = '\x06',
            ["Red"] = '\x07',
            ["Grey"] = '\x08',
            ["Yellow"] = '\x09',
            ["Silver"] = '\x0A',
            ["LightBlue"] = '\x0B',
            ["DarkBlue"] = '\x0C',
            ["Purple"] = '\x0E',
            ["LightRed"] = '\x0F',
            ["Orange"] = '\x10',
        };

    public static bool IsSupported(string? name)
        => string.Equals(name, "None", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Team", StringComparison.OrdinalIgnoreCase)
            || (name is not null && Named.ContainsKey(name));

    public static char? Resolve(string name, PlayerTeam team)
    {
        if (string.Equals(name, "None", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.Equals(name, "Team", StringComparison.OrdinalIgnoreCase))
        {
            return team switch
            {
                PlayerTeam.CounterTerrorist => '\x0B',
                PlayerTeam.Terrorist => '\x10',
                PlayerTeam.Spectator => '\x03',
                _ => Default,
            };
        }

        return Named.TryGetValue(name, out var color)
            ? color
            : throw new ArgumentOutOfRangeException(
                nameof(name), name, "Unsupported chat color.");
    }
}
