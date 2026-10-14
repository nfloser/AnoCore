namespace AnoCore.Modules.AnoVeto;

/// <summary>
/// Session-independent preset catalog with owner isolation and optimistic revisions.
/// In-memory implementation for tests; durable deployments require a transactional adapter.
/// </summary>
public sealed class AnoVetoPresetCatalog : IAnoVetoPresetRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<(ulong Owner, string Key), Entry> _entries = [];
    public const int MaxPresetsPerOwner = 32;

    public IReadOnlyList<(AnoVetoPreset Preset, long Revision)> List(ulong owner)
    {
        if (owner == 0) throw new ArgumentOutOfRangeException(nameof(owner));
        lock (_gate)
            return _entries.Where(x => x.Key.Owner == owner)
                .OrderBy(x => x.Value.Preset.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => (x.Value.Preset, x.Value.Revision)).ToArray();
    }

    public bool TryGet(ulong owner, string name, out AnoVetoPreset? preset, out long revision)
    {
        ValidateKey(owner, name);
        lock (_gate)
        {
            if (_entries.TryGetValue((owner, Normalize(name)), out var entry))
            {
                preset = entry.Preset;
                revision = entry.Revision;
                return true;
            }
        }
        preset = null;
        revision = 0;
        return false;
    }

    public long Create(AnoVetoPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        lock (_gate)
        {
            var key = (preset.OwnerSteamId64, Normalize(preset.Name));
            if (_entries.ContainsKey(key)) throw new InvalidOperationException("Preset name already exists.");
            if (_entries.Keys.Count(k => k.Owner == preset.OwnerSteamId64) >= MaxPresetsPerOwner)
                throw new InvalidOperationException("Preset limit reached.");
            _entries.Add(key, new Entry(preset, 1));
            return 1;
        }
    }

    public long Replace(ulong owner, string existingName, long expectedRevision, AnoVetoPreset replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ValidateKey(owner, existingName);
        if (owner != replacement.OwnerSteamId64)
            throw new UnauthorizedAccessException("A preset cannot change owners.");
        lock (_gate)
        {
            var oldKey = (owner, Normalize(existingName));
            if (!_entries.TryGetValue(oldKey, out var current) || current.Revision != expectedRevision)
                throw new InvalidOperationException("Preset revision conflict.");
            var newKey = (owner, Normalize(replacement.Name));
            if (newKey != oldKey && _entries.ContainsKey(newKey))
                throw new InvalidOperationException("Preset name already exists.");
            _entries.Remove(oldKey);
            var revision = checked(current.Revision + 1);
            _entries.Add(newKey, new Entry(replacement, revision));
            return revision;
        }
    }

    public void Delete(ulong owner, string name, long expectedRevision)
    {
        ValidateKey(owner, name);
        lock (_gate)
        {
            var key = (owner, Normalize(name));
            if (!_entries.TryGetValue(key, out var current) || current.Revision != expectedRevision)
                throw new InvalidOperationException("Preset revision conflict.");
            _entries.Remove(key);
        }
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();
    private static void ValidateKey(ulong owner, string name)
    {
        if (owner == 0) throw new ArgumentOutOfRangeException(nameof(owner));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > AnoVetoPreset.MaxNameLength)
            throw new ArgumentException("Invalid preset name.", nameof(name));
    }

    public ValueTask<IReadOnlyList<(AnoVetoPreset Preset, long Revision)>> ListAsync(
        ulong ownerSteamId64, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(List(ownerSteamId64));
    }

    public ValueTask<(AnoVetoPreset Preset, long Revision)?> GetAsync(
        ulong ownerSteamId64, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<(AnoVetoPreset Preset, long Revision)?>(
            TryGet(ownerSteamId64, name, out var preset, out var revision)
                ? (preset!, revision)
                : null);
    }

    public ValueTask<long> CreateAsync(AnoVetoPreset preset, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Create(preset));
    }

    public ValueTask<long> ReplaceAsync(
        ulong ownerSteamId64, string existingName, long expectedRevision,
        AnoVetoPreset replacement, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Replace(ownerSteamId64, existingName, expectedRevision, replacement));
    }

    public ValueTask DeleteAsync(
        ulong ownerSteamId64, string name, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delete(ownerSteamId64, name, expectedRevision);
        return ValueTask.CompletedTask;
    }

    private sealed record Entry(AnoVetoPreset Preset, long Revision);
}
