using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Stats;

public sealed record StatisticsResetResult(
    DateTimeOffset PreviousCutoffUtc,
    DateTimeOffset CurrentCutoffUtc,
    Guid AuditId);

public interface IStatisticsResetAdministrationService
{
    ValueTask<StatisticsResetResult> ResetAsync(
        PlayerId targetId,
        PlayerId? actorId,
        string reason,
        DateTimeOffset resetAtUtc,
        CancellationToken cancellationToken = default);
}
