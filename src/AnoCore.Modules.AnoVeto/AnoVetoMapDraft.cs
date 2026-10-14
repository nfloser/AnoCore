namespace AnoCore.Modules.AnoVeto;

/// <summary>
/// Validates the eight-map draft independently of Panorama input and saved preset storage.
/// A draft cannot modify the current public ballot until it is explicitly confirmed.
/// </summary>
public sealed class AnoVetoMapDraft
{
    public const int RequiredMaps = 8;
    private readonly Dictionary<string, string> _catalog;
    private readonly List<string> _selected = [];

    public AnoVetoMapDraft(IEnumerable<(string MapId, string DisplayName)> availableMaps)
    {
        ArgumentNullException.ThrowIfNull(availableMaps);
        _catalog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (mapId, displayName) in availableMaps)
        {
            if (string.IsNullOrWhiteSpace(mapId) || string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Map IDs and display names must be nonempty.", nameof(availableMaps));
            if (!_catalog.TryAdd(mapId, displayName))
                throw new ArgumentException("Map IDs must be unique.", nameof(availableMaps));
        }

        if (_catalog.Count < RequiredMaps)
            throw new ArgumentException("At least eight eligible maps are required.", nameof(availableMaps));
    }

    public IReadOnlyList<string> SelectedMapIds => _selected.ToArray();
    public bool CanConfirm => _selected.Count == RequiredMaps;
    public int AvailableMapCount => _catalog.Count;

    public bool Toggle(string mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId) || !_catalog.ContainsKey(mapId))
            return false;

        var existing = _selected.FindIndex(id => string.Equals(id, mapId, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            _selected.RemoveAt(existing);
            return true;
        }

        if (CanConfirm)
            return false;
        _selected.Add(_catalog.Keys.First(id => string.Equals(id, mapId, StringComparison.OrdinalIgnoreCase)));
        return true;
    }

    public IReadOnlyList<(string MapId, string DisplayName)> Browse(string? filter, int offset, int pageSize)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));

        var term = filter?.Trim() ?? string.Empty;
        return _catalog
            .Where(map => map.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
                || map.Value.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(map => map.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(map => map.Key, StringComparer.OrdinalIgnoreCase)
            .Skip(offset)
            .Take(pageSize)
            .Select(map => (map.Key, map.Value))
            .ToArray();
    }

    public IReadOnlyList<string> Confirm()
    {
        if (!CanConfirm)
            throw new InvalidOperationException("A veto draft requires exactly eight different maps.");
        return SelectedMapIds;
    }
}
