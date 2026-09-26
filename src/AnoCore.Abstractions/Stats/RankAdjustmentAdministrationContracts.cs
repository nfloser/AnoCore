using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Stats;

public enum RankAdjustmentAdminOperation
{
    Give = 1,
    Take = 2,
    Set = 3,
    Reset = 4,
}

public sealed record RankAdjustmentAdminResult(
    long PreviousPoints,
    long CurrentPoints,
    Guid AuditId);

public interface IRankAdjustmentAdministrationService
{
    ValueTask<RankAdjustmentAdminResult> ApplyAsync(
        RankAdjustmentAdminOperation operation,
        PlayerId targetId,
        long points,
        PlayerId? actorId,
        string reason,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default);
}
