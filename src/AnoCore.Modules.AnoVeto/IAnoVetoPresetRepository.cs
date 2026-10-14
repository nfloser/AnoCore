namespace AnoCore.Modules.AnoVeto;

/// <summary>
/// Storage-independent contract for durable owner-scoped veto presets.
/// Implementations must commit each mutation atomically with a revision check.
/// </summary>
public interface IAnoVetoPresetRepository
{
    ValueTask<IReadOnlyList<(AnoVetoPreset Preset, long Revision)>> ListAsync(
        ulong ownerSteamId64, CancellationToken cancellationToken = default);

    ValueTask<(AnoVetoPreset Preset, long Revision)?> GetAsync(
        ulong ownerSteamId64, string name, CancellationToken cancellationToken = default);

    ValueTask<long> CreateAsync(
        AnoVetoPreset preset, CancellationToken cancellationToken = default);

    ValueTask<long> ReplaceAsync(
        ulong ownerSteamId64, string existingName, long expectedRevision,
        AnoVetoPreset replacement, CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(
        ulong ownerSteamId64, string name, long expectedRevision,
        CancellationToken cancellationToken = default);
}
