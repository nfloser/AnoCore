using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Stats;

public sealed record RankPointAdjustment(
    PlayerId PlayerId,
    long Points,
    PlayerId? UpdatedBy,
    DateTimeOffset UpdatedAtUtc);

public interface IRankAdjustmentRepository
{
    ValueTask<RankPointAdjustment?> ReadAsync(
        PlayerId playerId, CancellationToken cancellationToken = default);
    ValueTask SetAsync(PlayerId playerId, long points, PlayerId? updatedBy,
        DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default);
    ValueTask ResetAsync(PlayerId playerId, CancellationToken cancellationToken = default);
}
