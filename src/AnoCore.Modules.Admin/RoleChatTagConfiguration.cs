using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public sealed record RoleChatTagAppearance(string Text, string Color = "None");

public sealed record RoleChatTagGroup(string Group, int Priority, RoleChatTagAppearance Tag);

public sealed class RoleChatTagConfiguration
{
    public bool Enabled { get; set; }
    public RoleChatTagAppearance? DefaultTag { get; set; } = new("[ANOMEME]", "Green");
    public List<RoleChatTagGroup> Groups { get; set; } = [];
    public Dictionary<ulong, RoleChatTagAppearance> PlayerOverrides { get; set; } = [];

    public static RoleChatTagConfiguration Default => new();

    public static IReadOnlyCollection<string> Validate(RoleChatTagConfiguration configuration)
    {
        if (configuration is null) return ["Role chat tag configuration is required."];
        var errors = new List<string>();
        if (configuration.DefaultTag is not null)
            ValidateAppearance(configuration.DefaultTag, "DefaultTag", errors);
        if (configuration.Groups is null || configuration.Groups.Count > 32)
            errors.Add("Groups must contain at most 32 entries.");
        else
        {
            var groups = new HashSet<string>(StringComparer.Ordinal);
            var priorities = new HashSet<int>();
            foreach (var entry in configuration.Groups)
            {
                if (entry is null)
                {
                    errors.Add("Groups cannot contain null entries.");
                    continue;
                }
                var name = entry.Group;
                if (string.IsNullOrWhiteSpace(name) || name.Length is < 2 or > 64
                    || name[0] != '#' || name.Any(character => char.IsWhiteSpace(character)
                        || char.IsControl(character) || character is '{' or '}')
                    || !groups.Add(name))
                    errors.Add($"Groups[{name}].Group must be a unique exact CSS group name starting with # (2..64 characters).");
                if (entry.Priority is < -1000 or > 1000 || !priorities.Add(entry.Priority))
                    errors.Add($"Groups[{name}].Priority must be unique and between -1000 and 1000.");
                ValidateAppearance(entry.Tag, $"Groups[{name}].Tag", errors);
            }
        }
        if (configuration.PlayerOverrides is null || configuration.PlayerOverrides.Count > 32)
            errors.Add("PlayerOverrides must contain at most 32 entries.");
        else
            foreach (var (steamId, tag) in configuration.PlayerOverrides)
            {
                try { _ = new PlayerId(steamId); }
                catch (ArgumentException) { errors.Add("PlayerOverrides keys must be valid SteamID64 values."); }
                // Avoid including personal identities in configuration diagnostics.
                ValidateAppearance(tag, "PlayerOverrides.Tag", errors);
            }
        return errors;
    }

    private static void ValidateAppearance(RoleChatTagAppearance? tag, string path, List<string> errors)
    {
        if (tag is null || string.IsNullOrWhiteSpace(tag.Text) || tag.Text.Length > 24
            || tag.Text.Any(character => char.IsControl(character) || character is '{' or '}'))
            errors.Add($"{path}.Text must contain 1..24 printable characters without braces.");
        if (tag is null || !ChatColorPalette.IsSupported(tag.Color))
            errors.Add($"{path}.Color must be a supported native chat color.");
    }
}

public sealed class RoleChatTagPolicy
{
    private readonly bool _enabled;
    private readonly RoleChatTagAppearance? _defaultTag;
    private readonly RoleChatTagGroup[] _groups;
    private readonly Dictionary<ulong, RoleChatTagAppearance> _overrides;

    public bool Enabled => _enabled;

    private RoleChatTagPolicy(RoleChatTagConfiguration configuration)
    {
        _enabled = configuration.Enabled;
        _defaultTag = configuration.DefaultTag;
        _groups = configuration.Groups.OrderByDescending(entry => entry.Priority).ToArray();
        _overrides = new(configuration.PlayerOverrides);
    }

    public static RoleChatTagPolicy Compile(RoleChatTagConfiguration configuration)
    {
        var errors = RoleChatTagConfiguration.Validate(configuration);
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors), nameof(configuration));
        return new(configuration);
    }

    public string? Resolve(PlayerId player, IReadOnlyCollection<string> exactGroups, PlayerTeam team)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(exactGroups);
        if (!_enabled) return null;
        var tag = _overrides.GetValueOrDefault(player.SteamId64)
            ?? _groups.FirstOrDefault(entry => exactGroups.Contains(entry.Group, StringComparer.Ordinal))?.Tag
            ?? _defaultTag;
        if (tag is null) return null;
        var color = ChatColorPalette.Resolve(tag.Color, team);
        return color is null ? $"{tag.Text} " : $"{color}{tag.Text}{ChatColorPalette.Default} ";
    }
}
