using AnoCore.Abstractions.Localization;

namespace AnoCore.Runtime.Localization;

public sealed class LocalizationConfiguration
{
    public string FallbackLocale { get; set; } = "en";

    public Dictionary<string, Dictionary<string, string>> Catalogs { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new(StringComparer.Ordinal),
        };

    public static LocalizationConfiguration Default => new();

    public static IReadOnlyCollection<string> Validate(LocalizationConfiguration configuration)
    {
        if (configuration is null) return ["Localization configuration is required."];

        var errors = new List<string>();
        if (!IsLocale(configuration.FallbackLocale))
            errors.Add("FallbackLocale must be a valid bounded locale identifier.");

        if (configuration.Catalogs is null
            || configuration.Catalogs.Count is < 1 or > 32)
        {
            errors.Add("Catalogs must contain between 1 and 32 locales.");
            return errors;
        }

        var normalizedLocales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in configuration.Catalogs)
        {
            if (!IsLocale(pair.Key))
            {
                errors.Add($"Locale '{pair.Key}' is invalid.");
                continue;
            }

            var locale = NormalizeLocale(pair.Key);
            if (!normalizedLocales.Add(locale))
                errors.Add($"Locale '{pair.Key}' is duplicated after normalization.");

            if (pair.Value is null || pair.Value.Count > 2048)
            {
                errors.Add($"Locale '{pair.Key}' must contain at most 2048 messages.");
                continue;
            }

            foreach (var message in pair.Value)
            {
                if (!IsKey(message.Key))
                    errors.Add($"Localization key '{message.Key}' in '{pair.Key}' is invalid.");
                if (message.Value is null
                    || message.Value.Length > 2048
                    || message.Value.Any(char.IsControl))
                {
                    errors.Add(
                        $"Localization value '{message.Key}' in '{pair.Key}' must be printable and at most 2048 characters.");
                }
            }
        }

        if (IsLocale(configuration.FallbackLocale)
            && !normalizedLocales.Contains(NormalizeLocale(configuration.FallbackLocale))
            && !normalizedLocales.Contains(Language(NormalizeLocale(configuration.FallbackLocale))))
        {
            errors.Add("FallbackLocale must have an exact or language-level catalog.");
        }

        return errors;
    }

    public ILocalizationService CreateService()
    {
        var errors = Validate(this);
        if (errors.Count != 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(LocalizationConfiguration));

        var catalogs = Catalogs.ToDictionary(
            pair => NormalizeLocale(pair.Key),
            pair => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(
                pair.Value, StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);
        return new LocalizationService(FallbackLocale, catalogs);
    }

    internal static string NormalizeLocale(string locale)
        => locale.Trim().Replace('_', '-').ToLowerInvariant();

    private static string Language(string locale)
    {
        var separator = locale.IndexOf('-');
        return separator < 0 ? locale : locale[..separator];
    }

    private static bool IsLocale(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().Replace('_', '-');
        return normalized.Length <= 32
            && normalized.All(character =>
                char.IsAsciiLetterOrDigit(character) || character == '-')
            && normalized[0] != '-'
            && normalized[^1] != '-';
    }

    private static bool IsKey(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Trim().Length <= 128
            && value.Trim().All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-' or '_');
}
