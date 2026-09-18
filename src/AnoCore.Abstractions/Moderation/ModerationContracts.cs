using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Moderation;

[Flags]
public enum ModerationRestriction
{
    None = 0,
    Connect = 1 << 0,
    Voice = 1 << 1,
    Chat = 1 << 2,
}

public enum ModerationAuditAction
{
    Applied = 1,
    Revoked = 2,
}

public sealed record ModerationSanction
{
    public ModerationSanction(
        Guid id,
        PlayerId targetId,
        PlayerId? actorId,
        ModerationRestriction restriction,
        string reason,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc = null,
        DateTimeOffset? revokedAtUtc = null,
        PlayerId? revokedById = null,
        string? revocationReason = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A moderation sanction id cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(targetId);
        if (!IsSingleRestriction(restriction))
        {
            throw new ArgumentOutOfRangeException(nameof(restriction), "A persisted sanction must contain exactly one restriction.");
        }

        var normalizedReason = ModerationValidation.NormalizeReason(reason);

        var created = createdAtUtc.ToUniversalTime();
        var expires = expiresAtUtc?.ToUniversalTime();
        var revoked = revokedAtUtc?.ToUniversalTime();
        if (expires is not null && expires <= created)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Expiry must be later than creation.");
        }

        if (revoked is not null && revoked < created)
        {
            throw new ArgumentOutOfRangeException(nameof(revokedAtUtc), "Revocation cannot precede creation.");
        }

        if (revoked is null && (revokedById is not null || revocationReason is not null))
        {
            throw new ArgumentException(
                "Revocation metadata requires a revocation timestamp.",
                nameof(revokedAtUtc));
        }

        if (revoked is not null && string.IsNullOrWhiteSpace(revocationReason))
        {
            throw new ArgumentException("A revoked sanction requires a revocation reason.", nameof(revocationReason));
        }

        Id = id;
        TargetId = targetId;
        ActorId = actorId;
        Restriction = restriction;
        Reason = normalizedReason;
        CreatedAtUtc = created;
        ExpiresAtUtc = expires;
        RevokedAtUtc = revoked;
        RevokedById = revokedById;
        RevocationReason = revoked is null
            ? null
            : ModerationValidation.NormalizeReason(revocationReason!);
    }

    public Guid Id { get; }

    public PlayerId TargetId { get; }

    public PlayerId? ActorId { get; }

    public ModerationRestriction Restriction { get; }

    public string Reason { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset? ExpiresAtUtc { get; }

    public DateTimeOffset? RevokedAtUtc { get; }

    public PlayerId? RevokedById { get; }

    public string? RevocationReason { get; }

    public ModerationSanction Revoke(
        PlayerId? actorId,
        string reason,
        DateTimeOffset atUtc)
    {
        var revokedAt = atUtc.ToUniversalTime();
        if (!IsActiveAt(revokedAt))
        {
            throw new InvalidOperationException("Only an active moderation sanction can be revoked.");
        }

        return new ModerationSanction(
            Id,
            TargetId,
            ActorId,
            Restriction,
            Reason,
            CreatedAtUtc,
            ExpiresAtUtc,
            revokedAt,
            actorId,
            reason);
    }

    public bool IsActiveAt(DateTimeOffset atUtc)
    {
        var instant = atUtc.ToUniversalTime();
        return instant >= CreatedAtUtc
            && (ExpiresAtUtc is null || instant < ExpiresAtUtc.Value)
            && (RevokedAtUtc is null || instant < RevokedAtUtc.Value);
    }

    private static bool IsSingleRestriction(ModerationRestriction restriction)
        => restriction is ModerationRestriction.Connect
            or ModerationRestriction.Voice
            or ModerationRestriction.Chat;
}

public sealed record ModerationAuditEntry
{
    public ModerationAuditEntry(
        Guid id,
        PlayerId targetId,
        PlayerId? actorId,
        ModerationAuditAction action,
        ModerationRestriction restrictions,
        string reason,
        DateTimeOffset occurredAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A moderation audit id cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(targetId);
        if (action is not ModerationAuditAction.Applied and not ModerationAuditAction.Revoked)
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        ModerationValidation.ValidateRestrictions(restrictions);
        var normalizedReason = ModerationValidation.NormalizeReason(reason);

        Id = id;
        TargetId = targetId;
        ActorId = actorId;
        Action = action;
        Restrictions = restrictions;
        Reason = normalizedReason;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public Guid Id { get; }

    public PlayerId TargetId { get; }

    public PlayerId? ActorId { get; }

    public ModerationAuditAction Action { get; }

    public ModerationRestriction Restrictions { get; }

    public string Reason { get; }

    public DateTimeOffset OccurredAtUtc { get; }
}

public sealed record ModerationState(
    PlayerId TargetId,
    ModerationRestriction Restrictions,
    IReadOnlyList<ModerationSanction> ActiveSanctions);

public interface IModerationRepository
{
    ValueTask AddAsync(
        IReadOnlyCollection<ModerationSanction> sanctions,
        ModerationAuditEntry audit,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ModerationSanction>> GetActiveAsync(
        PlayerId targetId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ModerationSanction>> GetHistoryAsync(
        PlayerId targetId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ModerationSanction>> RevokeActiveAsync(
        PlayerId targetId,
        ModerationRestriction restrictions,
        PlayerId? actorId,
        string reason,
        DateTimeOffset atUtc,
        ModerationAuditEntry audit,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ModerationAuditEntry>> GetAuditHistoryAsync(
        PlayerId targetId,
        CancellationToken cancellationToken = default);
}

public interface IModerationService
{
    ValueTask<IReadOnlyList<ModerationSanction>> ApplyAsync(
        PlayerId targetId,
        PlayerId? actorId,
        ModerationRestriction restrictions,
        string reason,
        DateTimeOffset atUtc,
        DateTimeOffset? expiresAtUtc = null,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ModerationSanction>> RevokeAsync(
        PlayerId targetId,
        PlayerId? actorId,
        ModerationRestriction restrictions,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default);

    ValueTask<ModerationState> GetStateAsync(
        PlayerId targetId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ModerationSanction>> GetHistoryAsync(
        PlayerId targetId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ModerationAuditEntry>> GetAuditHistoryAsync(
        PlayerId targetId,
        CancellationToken cancellationToken = default);
}

public static class ModerationValidation
{
    public const int MaxReasonLength = 512;

    private const ModerationRestriction All =
        ModerationRestriction.Connect | ModerationRestriction.Voice | ModerationRestriction.Chat;

    public static void ValidateRestrictions(ModerationRestriction restrictions)
    {
        if (restrictions == ModerationRestriction.None || (restrictions & ~All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(restrictions), "At least one known moderation restriction is required.");
        }
    }

    public static string NormalizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A moderation reason is required.", nameof(reason));
        }

        var normalized = reason.Trim();
        if (normalized.Length > MaxReasonLength)
        {
            throw new ArgumentException(
                $"Moderation reasons cannot exceed {MaxReasonLength} characters.",
                nameof(reason));
        }

        return normalized;
    }
}
