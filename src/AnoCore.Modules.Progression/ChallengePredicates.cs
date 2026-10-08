using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression;

/// <summary>Exact durable event filters: OR within each list, AND between lists.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ChallengePredicates
{
    public const int MaximumValues = 16;
    public IReadOnlyList<string> Maps { get; init; } = [];
    public IReadOnlyList<string> Weapons { get; init; } = [];
    public IReadOnlyList<int> Hitgroups { get; init; } = [];

    public IReadOnlyList<PlayerTeam> AttackerTeams { get; init; } = [];
    public bool? Headshot { get; init; }
    public bool? NoScope { get; init; }
    public bool? ThroughSmoke { get; init; }
    public bool? AttackerBlind { get; init; }
    public int? PenetrationMinimum { get; init; }
    public decimal? DistanceMinimumMeters { get; init; }
    public decimal? DistanceMaximumMeters { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasFilters => Maps.Count > 0 || Weapons.Count > 0 || Hitgroups.Count > 0 || AttackerTeams.Count > 0
        || Headshot is not null || NoScope is not null || ThroughSmoke is not null || AttackerBlind is not null
        || PenetrationMinimum is not null || DistanceMinimumMeters is not null || DistanceMaximumMeters is not null;

    internal ChallengePredicates SnapshotFor(string id, ChallengeCounterSource source)
    {
        try { return Snapshot(source); }
        catch (ArgumentException exception) { throw new ArgumentException($"Challenge '{id}'.Predicates: {exception.Message}", exception); }
    }

    private ChallengePredicates Snapshot(ChallengeCounterSource source)
    {
        var maps = Keys(Maps, 128, false);
        var weapons = Keys(Weapons, 64, true);
        if (Hitgroups is null) throw new ArgumentException("Hitgroups cannot be null.");
        var hitgroups = Hitgroups.Take(MaximumValues + 1).ToArray();
        if (hitgroups.Length > MaximumValues || hitgroups.Any(value => value is < 0 or > 255)
            || hitgroups.Distinct().Count() != hitgroups.Length)
            throw new ArgumentException("Hitgroups require at most 16 unique values from 0 through 255.");
        var damage = source is ChallengeCounterSource.UtilityDamage or ChallengeCounterSource.DamageHealth;
        var deaths = source is ChallengeCounterSource.CombatKills or ChallengeCounterSource.CombatAssists;
        if (AttackerTeams is null) throw new ArgumentException("AttackerTeams cannot be null.");
        var teams = AttackerTeams.Take(3).ToArray();
        if (teams.Length > 2 || teams.Any(value => value is not PlayerTeam.Terrorist and not PlayerTeam.CounterTerrorist)
            || teams.Distinct().Count() != teams.Length)
            throw new ArgumentException("AttackerTeams requires unique T (2) or CT (3) values.");
        if (PenetrationMinimum is < 0 or > 32 || DistanceMinimumMeters is < 0 or > 10000
            || DistanceMaximumMeters is < 0 or > 10000 || DistanceMinimumMeters > DistanceMaximumMeters
            || DistanceMinimumMeters is { } minimum && decimal.Round(minimum, 4) != minimum
            || DistanceMaximumMeters is { } maximum && decimal.Round(maximum, 4) != maximum)
            throw new ArgumentException("PenetrationMinimum requires 0-32; distance bounds require ordered 0-10000 meters with four decimal places.");
        if (weapons.Length > 0 && !damage && !deaths || hitgroups.Length > 0 && !damage
            || !deaths && (teams.Length > 0 || Headshot is not null || NoScope is not null || ThroughSmoke is not null
                || AttackerBlind is not null || PenetrationMinimum is not null
                || DistanceMinimumMeters is not null || DistanceMaximumMeters is not null))
            throw new ArgumentException("Challenge predicates require fields present in their durable counter source.");
        if (source == ChallengeCounterSource.UtilityDamage
            && weapons.Any(value => value is not "hegrenade" and not "inferno" and not "molotov" and not "incgrenade"))
            throw new ArgumentException("Utility damage weapon predicates must name utility weapons.");
        return new()
        {
            Maps = Array.AsReadOnly(maps.Order(StringComparer.Ordinal).ToArray()),
            Weapons = Array.AsReadOnly(weapons.Order(StringComparer.Ordinal).ToArray()),
            Hitgroups = Array.AsReadOnly(hitgroups.Order().ToArray()),
            AttackerTeams = Array.AsReadOnly(teams.Order().ToArray()),
            Headshot = Headshot,
            NoScope = NoScope,
            ThroughSmoke = ThroughSmoke,
            AttackerBlind = AttackerBlind,
            PenetrationMinimum = PenetrationMinimum,
            DistanceMinimumMeters = DistanceMinimumMeters,
            DistanceMaximumMeters = DistanceMaximumMeters,
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
