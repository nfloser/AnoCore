using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Stats;

public sealed record CombatDeath
{
    public CombatDeath(Guid eventId, PlayerId victimId, PlayerId? attackerId,
        PlayerId? assisterId, DateTimeOffset occurredAtUtc, bool isTeamKill = false)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("A combat event id is required.", nameof(eventId));
        EventId = eventId;
        VictimId = victimId ?? throw new ArgumentNullException(nameof(victimId));
        AttackerId = attackerId == victimId ? null : attackerId;
        IsTeamKill = isTeamKill && AttackerId is not null;
        AssisterId = AttackerId is null || IsTeamKill || assisterId == victimId || assisterId == AttackerId
            ? null : assisterId;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public Guid EventId { get; }
    public PlayerId VictimId { get; }
    public PlayerId? AttackerId { get; }
    public PlayerId? AssisterId { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public bool IsTeamKill { get; }
}

public sealed record CombatTotals(long Kills, long Deaths, long Assists);

public sealed record CombatRankEntry(PlayerId PlayerId, long Kills, int Position,
    string? DisplayName = null);

public sealed record CombatCountRankEntry(PlayerId PlayerId, long Count, int Position,
    string? DisplayName = null);

public sealed record CombatScoreRankEntry(PlayerId PlayerId, long Points, int Position,
    string? DisplayName = null);

public sealed record CombatWeaponFireEvent
{
    public CombatWeaponFireEvent(Guid eventId, PlayerId playerId, DateTimeOffset occurredAtUtc,
        string mapName, string weapon)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("A combat event id is required.", nameof(eventId));
        EventId = eventId;
        PlayerId = playerId ?? throw new ArgumentNullException(nameof(playerId));
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        MapName = CombatDetailContractValidation.NormalizeRequired(mapName, 128, nameof(mapName));
        Weapon = CombatDetailContractValidation.NormalizeRequired(weapon, 64, nameof(weapon));
    }

    public Guid EventId { get; }
    public PlayerId PlayerId { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public string MapName { get; }
    public string Weapon { get; }
}

public sealed record CombatDamageEvent
{
    public CombatDamageEvent(Guid eventId, PlayerId victimId, PlayerId? attackerId,
        DateTimeOffset occurredAtUtc, string mapName, string weapon, int hitgroup,
        int damageHealth, int damageArmor, bool isTeamDamage = false)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("A combat event id is required.", nameof(eventId));
        if (hitgroup is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(hitgroup));
        if (damageHealth is < 0 or > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(damageHealth));
        if (damageArmor is < 0 or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(damageArmor));

        EventId = eventId;
        VictimId = victimId ?? throw new ArgumentNullException(nameof(victimId));
        AttackerId = attackerId;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        MapName = CombatDetailContractValidation.NormalizeRequired(mapName, 128, nameof(mapName));
        Weapon = CombatDetailContractValidation.NormalizeRequired(weapon, 64, nameof(weapon));
        Hitgroup = hitgroup;
        DamageHealth = damageHealth;
        DamageArmor = damageArmor;
        IsTeamDamage = isTeamDamage && AttackerId is not null && AttackerId != VictimId;
    }

    public Guid EventId { get; }
    public PlayerId VictimId { get; }
    public PlayerId? AttackerId { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public string MapName { get; }
    public string Weapon { get; }
    public int Hitgroup { get; }
    public int DamageHealth { get; }
    public int DamageArmor { get; }
    public bool IsTeamDamage { get; }
    public bool IsSelfDamage => AttackerId == VictimId;
}

public sealed record CombatDetailFilter
{
    public CombatDetailFilter(string? mapName = null, string? weapon = null,
        bool includeTeamDamage = false, bool includeSelfDamage = false)
    {
        MapName = CombatDetailContractValidation.NormalizeOptional(mapName, 128, nameof(mapName));
        Weapon = CombatDetailContractValidation.NormalizeOptional(weapon, 64, nameof(weapon));
        IncludeTeamDamage = includeTeamDamage;
        IncludeSelfDamage = includeSelfDamage;
    }

    public string? MapName { get; }
    public string? Weapon { get; }
    public bool IncludeTeamDamage { get; }
    public bool IncludeSelfDamage { get; }
}

public sealed record CombatDetailTotals(
    long Shots,
    long Hits,
    long DamageHealth,
    long DamageArmor,
    long HeadHits);

public sealed record CombatHitgroupTotals(
    int Hitgroup,
    long Hits,
    long DamageHealth,
    long DamageArmor);

public interface ICombatDetailRepository : ICombatRepository
{
    ValueTask RecordWeaponFireAsync(CombatWeaponFireEvent weaponFire,
        CancellationToken cancellationToken = default);

    ValueTask RecordDamageAsync(CombatDamageEvent damage,
        CancellationToken cancellationToken = default);

    ValueTask<CombatDetailTotals> ReadDetailsAsync(PlayerId playerId,
        CombatDetailFilter? filter = null, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<CombatHitgroupTotals>> ReadHitgroupsAsync(PlayerId playerId,
        CombatDetailFilter? filter = null, CancellationToken cancellationToken = default);
}

internal static class CombatDetailContractValidation
{
    public static string NormalizeRequired(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty statistic key is required.", paramName);
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new ArgumentException($"Statistic key must be at most {maxLength} characters.", paramName);
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Statistic key cannot contain control characters.", paramName);
        return normalized;
    }

    public static string? NormalizeOptional(string? value, int maxLength, string paramName)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeRequired(value, maxLength, paramName);
}

public interface ICombatRepository
{
    ValueTask RecordAsync(CombatDeath death, CancellationToken cancellationToken = default);
    ValueTask<CombatTotals> ReadAsync(PlayerId playerId,
        CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<CombatRankEntry>> GetTopKillsAsync(int limit, int offset,
        CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopDeathsAsync(int limit, int offset,
        CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopAssistsAsync(int limit, int offset,
        CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<CombatScoreRankEntry>> GetTopScoresAsync(
        int killPoints, int assistPoints, int deathPenalty, int limit, int offset,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Combat score ranking is not supported.");
    ValueTask<CombatScoreRankEntry?> GetScorePlacementAsync(PlayerId playerId,
        int killPoints, int assistPoints, int deathPenalty,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Combat score placement is not supported.");
}
