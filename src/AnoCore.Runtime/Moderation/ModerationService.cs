using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Players;

namespace AnoCore.Runtime.Moderation;

public sealed class ModerationService : IModerationService
{
    private static readonly ModerationRestriction[] SingleRestrictions =
    [
        ModerationRestriction.Connect,
        ModerationRestriction.Voice,
        ModerationRestriction.Chat,
    ];

    private readonly IModerationRepository _repository;

    public ModerationService(IModerationRepository repository)
        => _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async ValueTask<IReadOnlyList<ModerationSanction>> ApplyAsync(
        PlayerId targetId,
        PlayerId? actorId,
        ModerationRestriction restrictions,
        string reason,
        DateTimeOffset atUtc,
        DateTimeOffset? expiresAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        ModerationValidation.ValidateRestrictions(restrictions);
        var normalizedReason = NormalizeReason(reason);
        var createdAt = atUtc.ToUniversalTime();
        var expiresAt = expiresAtUtc?.ToUniversalTime();
        if (expiresAt is not null && expiresAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Expiry must be later than the sanction creation time.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var sanctions = SingleRestrictions
            .Where(restriction => restrictions.HasFlag(restriction))
            .Select(restriction => new ModerationSanction(
                Guid.NewGuid(),
                targetId,
                actorId,
                restriction,
                normalizedReason,
                createdAt,
                expiresAt))
            .OrderBy(value => value.Restriction)
            .ToArray();

        var audit = new ModerationAuditEntry(
            Guid.NewGuid(),
            targetId,
            actorId,
            ModerationAuditAction.Applied,
            restrictions,
            normalizedReason,
            createdAt);

        await _repository.AddAsync(sanctions, audit, cancellationToken).ConfigureAwait(false);
        return sanctions;
    }

    public async ValueTask<IReadOnlyList<ModerationSanction>> RevokeAsync(
        PlayerId targetId,
        PlayerId? actorId,
        ModerationRestriction restrictions,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        ModerationValidation.ValidateRestrictions(restrictions);
        var normalizedReason = NormalizeReason(reason);
        var revokedAt = atUtc.ToUniversalTime();
        cancellationToken.ThrowIfCancellationRequested();

        var audit = new ModerationAuditEntry(
            Guid.NewGuid(),
            targetId,
            actorId,
            ModerationAuditAction.Revoked,
            restrictions,
            normalizedReason,
            revokedAt);

        var revoked = await _repository.RevokeActiveAsync(
            targetId,
            restrictions,
            actorId,
            normalizedReason,
            revokedAt,
            audit,
            cancellationToken).ConfigureAwait(false);

        return OrderSanctions(revoked);
    }

    public async ValueTask<ModerationState> GetStateAsync(
        PlayerId targetId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        var instant = atUtc.ToUniversalTime();
        var active = (await _repository.GetActiveAsync(targetId, instant, cancellationToken).ConfigureAwait(false))
            .Where(value => value.TargetId == targetId && value.IsActiveAt(instant))
            .OrderBy(value => value.CreatedAtUtc)
            .ThenBy(value => value.Id)
            .ToArray();

        var restrictions = active.Aggregate(
            ModerationRestriction.None,
            (current, sanction) => current | sanction.Restriction);

        return new ModerationState(targetId, restrictions, active);
    }

    public async ValueTask<IReadOnlyList<ModerationSanction>> GetHistoryAsync(
        PlayerId targetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        var history = await _repository.GetHistoryAsync(targetId, cancellationToken).ConfigureAwait(false);
        return OrderSanctions(history);
    }

    public async ValueTask<IReadOnlyList<ModerationAuditEntry>> GetAuditHistoryAsync(
        PlayerId targetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        var history = await _repository.GetAuditHistoryAsync(targetId, cancellationToken).ConfigureAwait(false);
        return history
            .Where(value => value.TargetId == targetId)
            .OrderBy(value => value.OccurredAtUtc)
            .ThenBy(value => value.Id)
            .ToArray();
    }

    private static IReadOnlyList<ModerationSanction> OrderSanctions(IEnumerable<ModerationSanction> sanctions)
        => sanctions
            .OrderBy(value => value.CreatedAtUtc)
            .ThenBy(value => value.Id)
            .ToArray();

    private static string NormalizeReason(string reason)
        => ModerationValidation.NormalizeReason(reason);
}
