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

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A moderation reason is required.", nameof(reason));
        }

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

        if (revoked is not null && string.IsNullOrWhiteSpace(revocationReason))
        {
            throw new ArgumentException("A revoked sanction requires a revocation reason.", nameof(revocationReason));
        }

        Id = id;
        TargetId = targetId;
        ActorId = actorId;
        Restriction = restriction;
        Reason = reason.Trim();
        CreatedAtUtc = created;
        ExpiresAtUtc = expires;
        RevokedAtUtc = revoked;
        RevokedById = revokedById;
        RevocationReason = string.IsNullOrWhiteSpace(revocationReason) ? null : revocationReason.Trim();
    }

    public Guid Id { get; init; }

    public PlayerId TargetId { get; init; }

    public PlayerId? ActorId { get; init; }

    public ModerationRestriction Restriction { get; init; }

    public string Reason { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public DateTimeOffset? RevokedAtUtc { get; init; }

    public PlayerId? RevokedById { get; init; }

    public string? RevocationReason { get; init; }

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
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A moderation audit reason is required.", nameof(reason));
        }

        Id = id;
        TargetId = targetId;
        ActorId = actorId;
        Action = action;
        Restrictions = restrictions;
        Reason = reason.Trim();
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
    private const ModerationRestriction All =
        ModerationRestriction.Connect | ModerationRestriction.Voice | ModerationRestriction.Chat;

    public static void ValidateRestrictions(ModerationRestriction restrictions)
    {
        if (restrictions == ModerationRestriction.None || (restrictions & ~All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(restrictions), "At least one known moderation restriction is required.");
        }
    }
}
