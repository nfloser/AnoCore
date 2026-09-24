using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Players;

namespace AnoCore.Runtime.Auditing;

public sealed class AdminAuditService : IAdminAuditService
{
    private readonly IAdminAuditRepository _repository;

    public AdminAuditService(IAdminAuditRepository repository)
        => _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async ValueTask<AdminAuditEntry> RecordAsync(
        AdminActionId action,
        PlayerId? actorId,
        PlayerId? targetId,
        string reason,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = new AdminAuditEntry(Guid.NewGuid(), action, actorId, targetId, reason, occurredAtUtc);
        await _repository.AppendAsync(entry, cancellationToken).ConfigureAwait(false);
        return entry;
    }

    public async ValueTask<IReadOnlyList<AdminAuditEntry>> GetRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        AdminAuditValidation.ValidateLimit(limit);
        cancellationToken.ThrowIfCancellationRequested();
        var entries = await _repository.GetRecentAsync(limit, cancellationToken).ConfigureAwait(false);
        return entries.OrderBy(entry => entry.OccurredAtUtc).ThenBy(entry => entry.Id).ToArray();
    }

    public async ValueTask<IReadOnlyList<AdminAuditEntry>> GetTargetHistoryAsync(
        PlayerId targetId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        AdminAuditValidation.ValidateLimit(limit);
        cancellationToken.ThrowIfCancellationRequested();
        var entries = await _repository.GetTargetHistoryAsync(targetId, limit, cancellationToken).ConfigureAwait(false);
        return entries.Where(entry => entry.TargetId == targetId)
            .OrderBy(entry => entry.OccurredAtUtc).ThenBy(entry => entry.Id).ToArray();
    }
}
