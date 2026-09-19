using System.Collections.Concurrent;
using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Players;

namespace AnoCore.Runtime.Moderation;

public sealed class ModerationService : IModerationService, IModerationSnapshotProvider
{
    private static readonly ModerationRestriction[] SingleRestrictions =
    [
        ModerationRestriction.Connect,
        ModerationRestriction.Voice,
        ModerationRestriction.Chat,
    ];

    private const int SnapshotGateCount = 64;

    private readonly IModerationRepository _repository;
    private readonly ConcurrentDictionary<PlayerId, ModerationSanction[]> _snapshots = [];
    private readonly SemaphoreSlim[] _snapshotGates =
        Enumerable.Range(0, SnapshotGateCount).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

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

        var gate = GetSnapshotGate(targetId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _repository.AddAsync(sanctions, audit, cancellationToken).ConfigureAwait(false);
            UpdateSnapshotIfLoaded(
                targetId,
                current =>
                [
                    .. current.Where(value => value.IsActiveAt(createdAt)),
                    .. sanctions,
                ]);
            return sanctions;
        }
        finally
        {
            gate.Release();
        }
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

        var gate = GetSnapshotGate(targetId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var revoked = await _repository.RevokeActiveAsync(
                targetId,
                restrictions,
                actorId,
                normalizedReason,
                revokedAt,
                audit,
                cancellationToken).ConfigureAwait(false);

            var ordered = OrderSanctions(revoked);
            if (ordered.Count > 0)
            {
                var replacements = ordered.ToDictionary(value => value.Id);
                UpdateSnapshotIfLoaded(
                    targetId,
                    current => current
                        .Select(value => replacements.GetValueOrDefault(value.Id, value))
                        .ToArray());
            }

            return ordered;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<ModerationState> GetStateAsync(
        PlayerId targetId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        var instant = atUtc.ToUniversalTime();
        var gate = GetSnapshotGate(targetId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var active = (await _repository.GetActiveAsync(targetId, instant, cancellationToken).ConfigureAwait(false))
                .Where(value => value.TargetId == targetId && value.IsActiveAt(instant))
                .OrderBy(value => value.CreatedAtUtc)
                .ThenBy(value => value.Id)
                .ToArray();

            _snapshots[targetId] = active;
            var restrictions = GetRestrictions(active, instant);
            return new ModerationState(targetId, restrictions, active);
        }
        finally
        {
            gate.Release();
        }
    }

    public bool TryGetRestrictions(
        PlayerId targetId,
        DateTimeOffset atUtc,
        out ModerationRestriction restrictions)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        if (!_snapshots.TryGetValue(targetId, out var sanctions))
        {
            restrictions = ModerationRestriction.None;
            return false;
        }

        restrictions = GetRestrictions(sanctions, atUtc.ToUniversalTime());
        return true;
    }

    public void Invalidate(PlayerId targetId)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        _snapshots.TryRemove(targetId, out _);
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

    private SemaphoreSlim GetSnapshotGate(PlayerId targetId)
        => _snapshotGates[targetId.SteamId64 % SnapshotGateCount];

    private void UpdateSnapshotIfLoaded(
        PlayerId targetId,
        Func<ModerationSanction[], ModerationSanction[]> update)
    {
        if (_snapshots.TryGetValue(targetId, out var current))
        {
            _snapshots[targetId] = update(current);
        }
    }

    private static ModerationRestriction GetRestrictions(
        IEnumerable<ModerationSanction> sanctions,
        DateTimeOffset atUtc)
        => sanctions
            .Where(value => value.IsActiveAt(atUtc))
            .Aggregate(
                ModerationRestriction.None,
                (current, sanction) => current | sanction.Restriction);

    private static IReadOnlyList<ModerationSanction> OrderSanctions(IEnumerable<ModerationSanction> sanctions)
        => sanctions
            .OrderBy(value => value.CreatedAtUtc)
            .ThenBy(value => value.Id)
            .ToArray();

    private static string NormalizeReason(string reason)
        => ModerationValidation.NormalizeReason(reason);
}
