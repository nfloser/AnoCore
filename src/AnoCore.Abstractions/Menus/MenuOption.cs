using System.Text.RegularExpressions;

namespace AnoCore.Abstractions.Menus;

public sealed class MenuOption
{
    private static readonly Regex IdPattern = new(
        "^[a-z0-9][a-z0-9_-]{0,31}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public MenuOption(
        string id,
        string label,
        Func<MenuSelectionContext, ValueTask> onSelected,
        bool keepOpen = false)
    {
        if (string.IsNullOrWhiteSpace(id) || !IdPattern.IsMatch(id.Trim().ToLowerInvariant()))
        {
            throw new ArgumentException("Menu option IDs must contain only lowercase-safe characters.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("A menu option label is required.", nameof(label));
        }

        Id = id.Trim().ToLowerInvariant();
        Label = label.Trim();
        OnSelected = onSelected ?? throw new ArgumentNullException(nameof(onSelected));
        KeepOpen = keepOpen;
    }

    public string Id { get; }

    public string Label { get; }

    public bool KeepOpen { get; }

    public Func<MenuSelectionContext, ValueTask> OnSelected { get; }
}
