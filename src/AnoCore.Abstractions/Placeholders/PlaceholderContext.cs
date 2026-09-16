namespace AnoCore.Abstractions.Placeholders;

public sealed record PlaceholderContext(IReadOnlyDictionary<string, object?> Values)
{
    public static PlaceholderContext Empty { get; } = new(
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
}
