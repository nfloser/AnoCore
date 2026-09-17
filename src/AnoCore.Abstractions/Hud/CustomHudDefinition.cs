namespace AnoCore.Abstractions.Hud;

public sealed class CustomHudDefinition
{
    public CustomHudDefinition(
        CustomHudId id,
        string layoutResource,
        string rootPanelId,
        IReadOnlyCollection<string>? buttonIds = null,
        string visibleClass = "shown",
        bool captureInput = false)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        LayoutResource = ValidateLayoutResource(layoutResource);
        RootPanelId = ValidateToken(rootPanelId, nameof(rootPanelId));
        VisibleClass = ValidateToken(visibleClass, nameof(visibleClass));
        CaptureInput = captureInput;

        var normalizedButtons = (buttonIds ?? Array.Empty<string>())
            .Select(buttonId => ValidateToken(buttonId, nameof(buttonIds)))
            .ToArray();
        if (normalizedButtons.Distinct(StringComparer.Ordinal).Count() != normalizedButtons.Length)
        {
            throw new ArgumentException("Custom HUD button IDs must be unique.", nameof(buttonIds));
        }

        ButtonIds = Array.AsReadOnly(normalizedButtons);
    }

    public CustomHudId Id { get; }
    public string LayoutResource { get; }
    public string RootPanelId { get; }
    public string VisibleClass { get; }
    public bool CaptureInput { get; }
    public IReadOnlyCollection<string> ButtonIds { get; }

    private static string ValidateLayoutResource(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A Panorama layout resource is required.", nameof(value));
        }

        var normalized = value.Trim().Replace('\\', '/');
        if (!normalized.StartsWith("panorama/layout/custom_game/", StringComparison.Ordinal)
            || !normalized.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Custom HUD layouts must be .xml resources below panorama/layout/custom_game/.",
                nameof(value));
        }

        return normalized;
    }

    private static string ValidateToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Custom HUD panel/class identifiers cannot be empty.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ArgumentException(
                "Custom HUD panel/class identifiers may contain only ASCII letters, digits, '_' and '-'.",
                parameterName);
        }

        return normalized;
    }
}
