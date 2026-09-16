using System.Text.RegularExpressions;

namespace AnoCore.Abstractions.Settings;

public sealed record PlayerSettingKey<T>
{
    private static readonly Regex Pattern = new(
        "^[a-z0-9][a-z0-9_.-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public PlayerSettingKey(string name, T defaultValue)
    {
        if (string.IsNullOrWhiteSpace(name) || !Pattern.IsMatch(name.Trim()))
        {
            throw new ArgumentException("Setting names must contain only lowercase-safe characters.", nameof(name));
        }

        Name = name.Trim();
        DefaultValue = defaultValue;
    }

    public string Name { get; }

    public T DefaultValue { get; }
}
