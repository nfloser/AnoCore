using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Stats;

/// <summary>Optional native death evidence. A null distance means unavailable, never zero.</summary>
public sealed record CombatDeathContext
{
    public CombatDeathContext(string mapName, string weapon, PlayerTeam attackerTeam,
        bool headshot, bool noScope, bool throughSmoke, int penetrations, decimal? distanceMeters,
        bool attackerBlind = false)
    {
        if (!Enum.IsDefined(attackerTeam)) throw new ArgumentOutOfRangeException(nameof(attackerTeam));
        if (penetrations is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(penetrations));
        if (distanceMeters is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(distanceMeters));
        MapName = CombatDetailContractValidation.NormalizeRequired(mapName, 128, nameof(mapName));
        Weapon = CombatDetailContractValidation.NormalizeRequired(weapon, 64, nameof(weapon));
        AttackerTeam = attackerTeam;
        Headshot = headshot;
        NoScope = noScope;
        ThroughSmoke = throughSmoke;
        Penetrations = penetrations;
        DistanceMeters = distanceMeters is { } distance ? decimal.Round(distance, 4, MidpointRounding.ToEven) : null;
        AttackerBlind = attackerBlind;
    }

    public string MapName { get; }
    public string Weapon { get; }
    public PlayerTeam AttackerTeam { get; }
    public bool Headshot { get; }
    public bool NoScope { get; }
    public bool ThroughSmoke { get; }
    public int Penetrations { get; }
    public decimal? DistanceMeters { get; }
    public bool AttackerBlind { get; }
}
