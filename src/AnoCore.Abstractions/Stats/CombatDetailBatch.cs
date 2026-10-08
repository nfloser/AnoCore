namespace AnoCore.Abstractions.Stats;

/// <summary>A bounded immutable package of existing combat detail events.</summary>
public sealed class CombatDetailBatch
{
    public const int MaximumEvents = 256;

    public CombatDetailBatch(IEnumerable<CombatWeaponFireEvent> weaponFire,
        IEnumerable<CombatDamageEvent> damage)
    {
        ArgumentNullException.ThrowIfNull(weaponFire);
        ArgumentNullException.ThrowIfNull(damage);
        var shots = weaponFire.Take(MaximumEvents + 1).ToArray();
        var hits = damage.Take(MaximumEvents + 1).ToArray();
        if (shots.Length + hits.Length > MaximumEvents)
            throw new ArgumentOutOfRangeException(nameof(weaponFire), "A combat batch may contain at most 256 events.");
        if (shots.Any(value => value is null) || hits.Any(value => value is null))
            throw new ArgumentException("Combat batch entries cannot be null.");
        WeaponFire = Array.AsReadOnly(shots);
        Damage = Array.AsReadOnly(hits);
    }

    public IReadOnlyList<CombatWeaponFireEvent> WeaponFire { get; }
    public IReadOnlyList<CombatDamageEvent> Damage { get; }
    public int Count => WeaponFire.Count + Damage.Count;
}

/// <summary>Optional atomic bulk-write capability; legacy repository contracts remain unchanged.</summary>
public interface ICombatDetailBatchRepository
{
    ValueTask RecordDetailsAsync(CombatDetailBatch batch, CancellationToken cancellationToken = default);
}
