using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Stats;

public enum RankScoringMode : byte
{
    Derived = 0,
    EventLedger = 1,
}

public sealed record RankPointEvent
{
    public RankPointEvent(Guid sourceEventId, PlayerId playerId, string component,
        long points, DateTimeOffset occurredAtUtc, string mapName)
    {
        if (sourceEventId == Guid.Empty)
            throw new ArgumentException("A source event id is required.", nameof(sourceEventId));
        if (points is < -1_000_000 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(points));
        SourceEventId = sourceEventId;
        PlayerId = playerId ?? throw new ArgumentNullException(nameof(playerId));
        Component = Normalize(component, 64, nameof(component));
        var utc = occurredAtUtc.ToUniversalTime();
        OccurredAtUtc = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
        MapName = Normalize(mapName, 128, nameof(mapName));
        Points = points;
    }

    public Guid SourceEventId { get; }
    public PlayerId PlayerId { get; }
    public string Component { get; }
    public long Points { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public string MapName { get; }

    private static string Normalize(string? value, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty value is required.", name);
        var result = value.Trim();
        if (result.Length > maximum || result.Any(char.IsControl))
            throw new ArgumentException($"Value must contain at most {maximum} printable characters.", name);
        return result;
    }
}

public interface IRankPointEventRepository
{
    ValueTask<bool> RecordAsync(
        RankPointEvent pointEvent,
        CancellationToken cancellationToken = default);
}

public interface IRankEventScoreRepository : IGameplayRankScoreRepository
{
}
