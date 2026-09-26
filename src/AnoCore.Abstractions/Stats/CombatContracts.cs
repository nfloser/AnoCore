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
