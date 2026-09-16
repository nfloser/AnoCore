namespace AnoCore.Abstractions.Localization;

public interface ILocalizationService
{
    string Translate(
        string key,
        string? locale = null,
        IReadOnlyDictionary<string, object?>? arguments = null);
}
