namespace AnoCore.Modules.AnoVeto;

/// <summary>
/// Immutable, owner-scoped preset definition. Storage and authorization adapters must
/// enforce caller identity and revision checks before persisting or starting a vote.
/// </summary>
public sealed class AnoVetoPreset
{
    public const int MaxNameLength = 48;

    public AnoVetoPreset(ulong ownerSteamId64, string name, IEnumerable<string> mapIds)
    {
        if (ownerSteamId64 == 0) throw new ArgumentOutOfRangeException(nameof(ownerSteamId64));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength
            || name.Any(char.IsControl))
            throw new ArgumentException("Preset name must be printable and 1-48 characters.", nameof(name));
        ArgumentNullException.ThrowIfNull(mapIds);

        var ids = mapIds.ToArray();
        if (ids.Length != AnoVetoMapDraft.RequiredMaps
            || ids.Any(id => string.IsNullOrWhiteSpace(id))
            || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != AnoVetoMapDraft.RequiredMaps)
            throw new ArgumentException("Preset requires exactly eight distinct map IDs.", nameof(mapIds));

        OwnerSteamId64 = ownerSteamId64;
        Name = name.Trim();
        MapIds = Array.AsReadOnly(ids);
    }

    public ulong OwnerSteamId64 { get; }
    public string Name { get; }
    public IReadOnlyList<string> MapIds { get; }

    public AnoVetoPreset Rename(string name) => new(OwnerSteamId64, name, MapIds);
    public AnoVetoPreset ReplaceMaps(IEnumerable<string> mapIds) => new(OwnerSteamId64, Name, mapIds);
}
