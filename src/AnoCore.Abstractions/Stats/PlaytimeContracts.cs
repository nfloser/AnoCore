using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Stats;

public sealed record PlaytimeSession
{
    public PlaytimeSession(PlayerId playerId, PlayerSessionId sessionId,
        DateTimeOffset startedAtUtc, DateTimeOffset accountedUntilUtc,
        DateTimeOffset? closedAtUtc = null)
    {
        PlayerId = playerId ?? throw new ArgumentNullException(nameof(playerId));
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        AccountedUntilUtc = accountedUntilUtc.ToUniversalTime();
        ClosedAtUtc = closedAtUtc?.ToUniversalTime();
        if (AccountedUntilUtc < StartedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(accountedUntilUtc));
        if (ClosedAtUtc is not null && ClosedAtUtc < AccountedUntilUtc)
            throw new ArgumentOutOfRangeException(nameof(closedAtUtc));
    }

    public PlayerId PlayerId { get; }
    public PlayerSessionId SessionId { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset AccountedUntilUtc { get; }
    public DateTimeOffset? ClosedAtUtc { get; }
    public TimeSpan Accounted => AccountedUntilUtc - StartedAtUtc;

    public TimeSpan AccountedWithin(DateOnly utcDay)
    {
        var start = new DateTimeOffset(utcDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);
        var overlapStart = StartedAtUtc > start ? StartedAtUtc : start;
        var overlapEnd = AccountedUntilUtc < end ? AccountedUntilUtc : end;
        return overlapEnd > overlapStart ? overlapEnd - overlapStart : TimeSpan.Zero;
    }
}

public sealed record PlaytimeTotals(TimeSpan Total, TimeSpan Today);

public sealed record PlaytimeRankEntry(PlayerId PlayerId, TimeSpan Total, int Position);

public interface IPlaytimeRepository
{
    ValueTask OpenAsync(PlayerId playerId, PlayerSessionId sessionId, DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default);
    ValueTask AdvanceAsync(PlayerId playerId, PlayerSessionId sessionId, DateTimeOffset atUtc,
        bool close = false, CancellationToken cancellationToken = default);
    ValueTask<PlaytimeTotals> ReadAsync(PlayerId playerId, DateOnly utcDay,
        CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<PlaytimeRankEntry>> GetTopAsync(int limit, int offset,
        CancellationToken cancellationToken = default);
}
