namespace AnoCore.Abstractions.Menus;

public sealed class MenuDefinition
{
    public MenuDefinition(MenuId id, string title, IReadOnlyCollection<MenuOption> options)
        : this(id, title, options, null)
    {
    }

    public MenuDefinition(MenuId id, string title, IReadOnlyCollection<MenuOption> options, MenuDashboard? dashboard)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A menu title is required.", nameof(title));
        }

        ArgumentNullException.ThrowIfNull(options);

        var optionArray = options.ToArray();
        if (optionArray.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count() != optionArray.Length)
        {
            throw new ArgumentException("Menu option IDs must be unique within a menu.", nameof(options));
        }

        Id = id;
        Title = title.Trim();
        Options = optionArray;
        Dashboard = dashboard;
    }

    public MenuDashboard? Dashboard { get; }

    public MenuId Id { get; }

    public string Title { get; }

    public IReadOnlyList<MenuOption> Options { get; }
}
