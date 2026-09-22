using System.Text.RegularExpressions;
using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Auditing;

public sealed record AdminActionId
{
    public const int MaxLength = 64;

    private static readonly Regex ValidPattern = new(
        "^[a-z][a-z0-9._-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public AdminActionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An administrative action id is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > MaxLength || !ValidPattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "Administrative action ids must start with a letter and contain only lowercase letters, numbers, dots, underscores or hyphens.",
                nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record AdminAuditEntry
{
    public AdminAuditEntry(
        Guid id,
        AdminActionId action,
        PlayerId? actorId,
        PlayerId? targetId,
        string reason,
        DateTimeOffset occurredAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An administrative audit id cannot be empty.", nameof(id));
        }

        Action = action ?? throw new ArgumentNullException(nameof(action));
        Id = id;
        ActorId = actorId;
        TargetId = targetId;
        Reason = AdminAuditValidation.NormalizeReason(reason);
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public Guid Id { get; }

    public AdminActionId Action { get; }

    public PlayerId? ActorId { get; }

    public PlayerId? TargetId { get; }

    public string Reason { get; }

    public DateTimeOffset OccurredAtUtc { get; }
}

public interface IAdminAuditRepository
{
    ValueTask AppendAsync(
        AdminAuditEntry entry,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AdminAuditEntry>> GetRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AdminAuditEntry>> GetTargetHistoryAsync(
        PlayerId targetId,
        int limit = 100,
        CancellationToken cancellationToken = default);
}

public interface IAdminAuditService
{
    ValueTask<AdminAuditEntry> RecordAsync(
        AdminActionId action,
        PlayerId? actorId,
        PlayerId? targetId,
        string reason,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AdminAuditEntry>> GetRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AdminAuditEntry>> GetTargetHistoryAsync(
        PlayerId targetId,
        int limit = 100,
        CancellationToken cancellationToken = default);
}

public static class AdminAuditValidation
{
    public const int MaxReasonLength = 512;

    public static string NormalizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An administrative audit reason is required.", nameof(reason));
        }

        var normalized = reason.Trim();
        if (normalized.Length > MaxReasonLength)
        {
            throw new ArgumentException(
                $"Administrative audit reasons cannot exceed {MaxReasonLength} characters.",
                nameof(reason));
        }

        return normalized;
    }

    public static int ValidateLimit(int limit)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Audit query limits must be between 1 and 1000.");
        }

        return limit;
    }
}
