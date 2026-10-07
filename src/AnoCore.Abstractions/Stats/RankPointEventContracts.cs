using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Stats;

public enum RankScoreSource
{
    DerivedStatistics,
    EventLedger,
}

public sealed record RankPointAward(PlayerId PlayerId, long Points);

public sealed class RankPointEventBatch
{
    public const int MaximumAwards = 64;
    public const long MaximumAbsolutePoints = 1_000_000;

    private RankPointEventBatch(Guid eventId, string source, DateTimeOffset occurredAtUtc,
        IReadOnlyList<RankPointAward> awards)
    {
        EventId = eventId;
        Source = source;
        OccurredAtUtc = occurredAtUtc;
        Awards = awards;
    }

    public Guid EventId { get; }
    public string Source { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public IReadOnlyList<RankPointAward> Awards { get; }

    public static RankPointEventBatch Create(Guid eventId, string source,
        DateTimeOffset occurredAtUtc, IEnumerable<RankPointAward> awards)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("A rank event ID is required.", nameof(eventId));
        if (string.IsNullOrWhiteSpace(source) || source.Length > 64
            || source.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
            throw new ArgumentException("Rank event sources require 1-64 ASCII letters, digits, dots, dashes or underscores.", nameof(source));
        ArgumentNullException.ThrowIfNull(awards);
        var snapshot = awards.Take(MaximumAwards + 1).ToArray();
        if (snapshot.Length is < 1 or > MaximumAwards)
            throw new ArgumentException("Rank batches require between 1 and 64 awards.", nameof(awards));
        var players = new HashSet<PlayerId>();
        foreach (var award in snapshot)
        {
            if (award is null || award.PlayerId is null || !players.Add(award.PlayerId)
                || award.Points is < -MaximumAbsolutePoints or > MaximumAbsolutePoints)
                throw new ArgumentException("Rank awards require unique players and bounded signed points.", nameof(awards));
        }
        var utc = occurredAtUtc.ToUniversalTime();
        utc = new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
        return new RankPointEventBatch(eventId, source, utc,
            Array.AsReadOnly(snapshot.OrderBy(award => award.PlayerId.SteamId64).ToArray()));
    }
}

public sealed record RankPointEventResult(bool Applied, RankPointEventBatch Batch);

public interface IRankPointEventRepository
{
    ValueTask<RankPointEventResult> ApplyAsync(RankPointEventBatch batch,
        CancellationToken cancellationToken = default);
    ValueTask<RankPointEventBatch?> ReadAsync(Guid eventId,
        CancellationToken cancellationToken = default);
}
