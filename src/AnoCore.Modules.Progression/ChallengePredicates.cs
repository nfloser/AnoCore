namespace AnoCore.Modules.Progression;

/// <summary>Exact durable event filters: OR within each list, AND between lists.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ChallengePredicates
{
    public const int MaximumValues = 16;
    public IReadOnlyList<string> Maps { get; init; } = [];
    public IReadOnlyList<string> Weapons { get; init; } = [];
    public IReadOnlyList<int> Hitgroups { get; init; } = [];

    internal ChallengePredicates Snapshot(ChallengeCounterSource source)
    {
        var maps = Keys(Maps, 128, false);
        var weapons = Keys(Weapons, 64, true);
        if (Hitgroups is null) throw new ArgumentException("Hitgroups cannot be null.");
        var hitgroups = Hitgroups.Take(MaximumValues + 1).ToArray();
        if (hitgroups.Length > MaximumValues || hitgroups.Any(value => value is < 0 or > 255)
            || hitgroups.Distinct().Count() != hitgroups.Length)
            throw new ArgumentException("Hitgroups require at most 16 unique values from 0 through 255.");
        var damage = source is ChallengeCounterSource.UtilityDamage or ChallengeCounterSource.DamageHealth;
        if (maps.Length > 0 && source is ChallengeCounterSource.CombatKills or ChallengeCounterSource.CombatAssists
            || (weapons.Length > 0 || hitgroups.Length > 0) && !damage)
            throw new ArgumentException("Challenge predicates require fields present in their durable counter source.");
        if (source == ChallengeCounterSource.UtilityDamage
            && weapons.Any(value => value is not "hegrenade" and not "inferno" and not "molotov" and not "incgrenade"))
            throw new ArgumentException("Utility damage weapon predicates must name utility weapons.");
        return new()
        {
            Maps = Array.AsReadOnly(maps.Order(StringComparer.Ordinal).ToArray()),
            Weapons = Array.AsReadOnly(weapons.Order(StringComparer.Ordinal).ToArray()),
            Hitgroups = Array.AsReadOnly(hitgroups.Order().ToArray()),
        };
    }

    private static string[] Keys(IReadOnlyList<string>? values, int maximumLength, bool weapon)
    {
        if (values is null) throw new ArgumentException("Predicate lists cannot be null.");
        var snapshot = values.Take(MaximumValues + 1).ToArray();
        if (snapshot.Length > MaximumValues || snapshot.Any(value => string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength || weapon && value.StartsWith("weapon_", StringComparison.Ordinal)
            || value.Any(character => !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '/' or '.')))
            || snapshot.Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new ArgumentException("Predicate keys require at most 16 unique lowercase engine keys without weapon_ prefixes.");
        return snapshot;
    }
}
