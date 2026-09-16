using System.Globalization;
using AnoCore.Abstractions.Localization;

namespace AnoCore.Runtime.Localization;

public sealed class LocalizationService : ILocalizationService
{
    private readonly string _fallbackLocale;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _catalogs;

    public LocalizationService(
        string fallbackLocale,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> catalogs)
    {
        if (string.IsNullOrWhiteSpace(fallbackLocale))
        {
            throw new ArgumentException("A fallback locale is required.", nameof(fallbackLocale));
        }

        ArgumentNullException.ThrowIfNull(catalogs);
        _fallbackLocale = NormalizeLocale(fallbackLocale);
        _catalogs = catalogs.ToDictionary(
            pair => NormalizeLocale(pair.Key),
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public string Translate(
        string key,
        string? locale = null,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A localization key is required.", nameof(key));
        }

        var normalizedKey = key.Trim();
        var requestedLocale = NormalizeLocale(locale ?? _fallbackLocale);
        var template = Find(requestedLocale, normalizedKey)
            ?? Find(GetLanguage(requestedLocale), normalizedKey)
            ?? Find(_fallbackLocale, normalizedKey)
            ?? Find(GetLanguage(_fallbackLocale), normalizedKey);

        if (template is null)
        {
            return $"[[{normalizedKey}]]";
        }

        if (arguments is null || arguments.Count == 0)
        {
            return template;
        }

        var result = template;
        foreach (var argument in arguments)
        {
            var formatted = argument.Value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : argument.Value?.ToString() ?? string.Empty;
            result = result.Replace(
                $"{{{argument.Key}}}",
                formatted,
                StringComparison.Ordinal);
        }

        return result;
    }

    private string? Find(string locale, string key)
        => _catalogs.TryGetValue(locale, out var catalog)
            && catalog.TryGetValue(key, out var value)
                ? value
                : null;

    private static string NormalizeLocale(string locale)
        => locale.Trim().Replace('_', '-').ToLowerInvariant();

    private static string GetLanguage(string locale)
    {
        var separator = locale.IndexOf('-');
        return separator < 0 ? locale : locale[..separator];
    }
}
