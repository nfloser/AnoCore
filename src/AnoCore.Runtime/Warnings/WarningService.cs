using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Warnings;

namespace AnoCore.Runtime.Warnings;

public sealed class WarningService : IWarningService
{
    private readonly IWarningRepository _repository;

    public WarningService(IWarningRepository repository)
        => _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async ValueTask<WarningRecord> WarnAsync(
        PlayerId targetId, PlayerId? actorId, string reason, DateTimeOffset atUtc,
        DateTimeOffset? expiresAtUtc = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var warning = new WarningRecord(Guid.NewGuid(), targetId, actorId, reason, atUtc, expiresAtUtc);
        await _repository.InsertAsync(warning, cancellationToken).ConfigureAwait(false);
        return warning;
    }

    public async ValueTask<IReadOnlyList<WarningRecord>> GetActiveAsync(
        PlayerId targetId, DateTimeOffset atUtc, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        WarningValidation.ValidateLimit(limit);
        var warnings = await _repository.GetActiveAsync(targetId, atUtc, limit, cancellationToken)
            .ConfigureAwait(false);
        return Order(warnings.Where(value => value.TargetId == targetId && value.IsActiveAt(atUtc)));
    }

    public async ValueTask<IReadOnlyList<WarningRecord>> GetHistoryAsync(
        PlayerId targetId, int limit = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        WarningValidation.ValidateLimit(limit);
        var warnings = await _repository.GetHistoryAsync(targetId, limit, cancellationToken)
            .ConfigureAwait(false);
        return Order(warnings.Where(value => value.TargetId == targetId));
    }

    public async ValueTask<IReadOnlyList<WarningRecord>> ClearAsync(
        PlayerId targetId, PlayerId? actorId, string reason, DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        var normalized = WarningValidation.NormalizeReason(reason);
        var changed = await _repository.ClearActiveAsync(
            targetId, actorId, normalized, atUtc.ToUniversalTime(), cancellationToken)
            .ConfigureAwait(false);
        return Order(changed);
    }

    private static IReadOnlyList<WarningRecord> Order(IEnumerable<WarningRecord> warnings)
        => warnings.OrderBy(value => value.CreatedAtUtc).ThenBy(value => value.Id).ToArray();
}
