namespace AnoCore.Abstractions.Settings;

public sealed class PlayerToggleSetting
{
    public PlayerToggleSetting(PlayerSettingKey<bool> key, string label, string description)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Label = Validate(label, nameof(label), 64, required: true);
        Description = Validate(description, nameof(description), 256, required: false);
    }

    public PlayerSettingKey<bool> Key { get; }

    public string Label { get; }

    public string Description { get; }

    private static string Validate(
        string value, string parameter, int maxLength, bool required)
    {
        ArgumentNullException.ThrowIfNull(value, parameter);
        var trimmed = value.Trim();
        if ((required && trimmed.Length == 0)
            || trimmed.Length > maxLength
            || trimmed.Any(character => char.IsControl(character)
                || character is '{' or '}'))
            throw new ArgumentException("Setting display text is invalid.", parameter);
        return trimmed;
    }
}
