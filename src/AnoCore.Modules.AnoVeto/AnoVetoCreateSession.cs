namespace AnoCore.Modules.AnoVeto;

/// <summary>
/// Isolated per-player creation screen state. HUD callbacks must verify their player session
/// before applying an action, and no action in this model can publish a public vote.
/// </summary>
public sealed class AnoVetoCreateSession
{
    public enum Screen { Home, Create, Saved }

    public Screen CurrentScreen { get; private set; } = Screen.Home;
    public AnoVetoMapDraft Draft { get; }
    public string Filter { get; private set; } = string.Empty;

    public AnoVetoCreateSession(IEnumerable<(string MapId, string DisplayName)> availableMaps)
    {
        Draft = new AnoVetoMapDraft(availableMaps);
    }

    public void OpenCreate()
    {
        CurrentScreen = Screen.Create;
    }

    public void OpenSaved()
    {
        CurrentScreen = Screen.Saved;
    }

    public void Back()
    {
        CurrentScreen = Screen.Home;
        Filter = string.Empty;
    }

    public bool SelectMap(string mapId)
        => CurrentScreen == Screen.Create && Draft.Toggle(mapId);

    public void ApplyFilter(string? filter)
    {
        if (CurrentScreen != Screen.Create)
            throw new InvalidOperationException("Map filters only apply to the creation screen.");
        if (filter is { Length: > 80 })
            throw new ArgumentOutOfRangeException(nameof(filter));
        Filter = filter?.Trim() ?? string.Empty;
    }

    public IReadOnlyList<(string MapId, string DisplayName)> Browse(int offset, int pageSize)
        => CurrentScreen == Screen.Create
            ? Draft.Browse(Filter, offset, pageSize)
            : [];
}
