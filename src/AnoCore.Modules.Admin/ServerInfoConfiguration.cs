using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public sealed class ServerInfoConfiguration
{
    public bool Enabled { get; set; }
    public int WelcomeDelaySeconds { get; set; } = 10;
    public int IntervalSeconds { get; set; } = 180;
    public List<string> WelcomeMessages { get; set; } = ["{Green}[ANO] Willkommen {player.name}!"];
    public List<List<string>> InfoMessages { get; set; } = [["{Green}[ANO] Menü: {Yellow}!anomenu"]];

    public static IReadOnlyCollection<string> Validate(ServerInfoConfiguration value)
    {
        if (value is null) return ["Server info configuration is required."];
        var errors = new List<string>();
        if (value.WelcomeDelaySeconds is < 0 or > 120) errors.Add("WelcomeDelaySeconds must be 0..120.");
        if (value.IntervalSeconds is < 30 or > 86400) errors.Add("IntervalSeconds must be 30..86400.");
        ValidateLines(value.WelcomeMessages, 12, errors);
        if (value.InfoMessages is null || value.InfoMessages.Count > 32)
            errors.Add("InfoMessages must contain at most 32 blocks.");
        else foreach (var lines in value.InfoMessages) ValidateLines(lines, 8, errors);
        return errors;
    }

    private static void ValidateLines(List<string>? lines, int limit, List<string> errors)
    {
        if (lines is null || lines.Count > limit) { errors.Add($"Message block must contain at most {limit} lines."); return; }
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length > 240 || line.Any(char.IsControl))
            { errors.Add("Message lines must contain 1..240 printable characters."); continue; }
            if (!ServerInfoText.TryParse(line, out var parts))
            { errors.Add("Unbalanced message token braces."); continue; }
            var expandedLength = 1;
            foreach (var part in parts)
            {
                if (!part.IsToken) { expandedLength += part.Text.Length; continue; }
                var token = part.Text;
                if (token is not ("player.name" or "map.name" or "playtime.total")
                    && (!ChatColorPalette.IsSupported(token) || token.Equals("Team", StringComparison.OrdinalIgnoreCase)
                        || token.Equals("None", StringComparison.OrdinalIgnoreCase)))
                    errors.Add($"Unsupported server info token: {token}.");
                expandedLength += token switch
                {
                    "player.name" => 64,
                    "map.name" => 128,
                    "playtime.total" => 64,
                    _ => 1,
                };
            }
            if (expandedLength > 1023) errors.Add("Expanded message would exceed the native text limit.");
        }
    }
}

internal static class ServerInfoText
{
    internal readonly record struct Part(string Text, bool IsToken);

    internal static bool TryParse(string text, out List<Part> parts)
    {
        parts = [];
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '}') return false;
            if (text[index] != '{') continue;
            if (index > start) parts.Add(new(text[start..index], false));
            var close = text.IndexOf('}', index + 1);
            if (close < 0 || close == index + 1 || text.AsSpan(index + 1, close - index - 1).Contains('{'))
                return false;
            parts.Add(new(text[(index + 1)..close], true));
            index = close;
            start = close + 1;
        }
        if (start < text.Length) parts.Add(new(text[start..], false));
        return true;
    }

    internal static string Render(string template, PlayerSnapshot player, string map, string playtime)
    {
        if (!TryParse(template, out var parts)) throw new ArgumentException("Invalid server info template.", nameof(template));
        return string.Concat(parts.Select(part => !part.IsToken ? part.Text : part.Text switch
        {
            "player.name" => Clean(player.Name, 64),
            "map.name" => Clean(map, 128),
            "playtime.total" => playtime,
            var color => ChatColorPalette.Resolve(color, player.Team)!.Value.ToString(),
        })) + ChatColorPalette.Default;
    }

    private static string Clean(string value, int limit)
        => new(value.Take(limit).Select(c => char.IsControl(c) || c is '{' or '}' ? ' ' : c).ToArray());
}
