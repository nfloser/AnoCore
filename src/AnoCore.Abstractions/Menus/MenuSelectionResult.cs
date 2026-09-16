namespace AnoCore.Abstractions.Menus;

public sealed record MenuSelectionResult(bool Accepted, string? Error = null)
{
    public static MenuSelectionResult Success() => new(true);

    public static MenuSelectionResult Rejected(string error) => new(false, error);
}
