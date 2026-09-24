using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Warnings;

public sealed record WarningRecord
{
    public WarningRecord(
        Guid id,
        PlayerId targetId,
        PlayerId? actorId,
        string reason,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc = null,
        DateTimeOffset? clearedAtUtc = null,
        PlayerId? clearedById = null,
        string? clearReason = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("A warning id is required.", nameof(id));
        Id = id;
        TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
        ActorId = actorId;
        Reason = WarningValidation.NormalizeReason(reason);
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        ExpiresAtUtc = expiresAtUtc?.ToUniversalTime();
        ClearedAtUtc = clearedAtUtc?.ToUniversalTime();
        if (ExpiresAtUtc <= CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        if (ClearedAtUtc is null)
        {
            if (clearedById is not null || clearReason is not null)
                throw new ArgumentException("Clear metadata requires a clear timestamp.", nameof(clearedAtUtc));
        }
        else
        {
            if (ClearedAtUtc < CreatedAtUtc)
                throw new ArgumentOutOfRangeException(nameof(clearedAtUtc));
            clearReason = WarningValidation.NormalizeReason(clearReason!);
        }

        ClearedById = clearedById;
        ClearReason = clearReason;
    }

    public Guid Id { get; }
    public PlayerId TargetId { get; }
    public PlayerId? ActorId { get; }
    public string Reason { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset? ExpiresAtUtc { get; }
    public DateTimeOffset? ClearedAtUtc { get; }
    public PlayerId? ClearedById { get; }
    public string? ClearReason { get; }

    public bool IsActiveAt(DateTimeOffset atUtc)
    {
        var instant = atUtc.ToUniversalTime();
        return CreatedAtUtc <= instant
            && (ExpiresAtUtc is null || ExpiresAtUtc > instant)
            && (ClearedAtUtc is null || ClearedAtUtc > instant);
    }

    public WarningRecord Clear(PlayerId? actorId, string reason, DateTimeOffset atUtc)
    {
        if (!IsActiveAt(atUtc))
            throw new InvalidOperationException("Only an active warning can be cleared.");

        return new WarningRecord(Id, TargetId, ActorId, Reason, CreatedAtUtc, ExpiresAtUtc,
            atUtc, actorId, reason);
    }
}

public static class WarningValidation
{
    public const int MaxReasonLength = 512;

    public static string NormalizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A warning reason is required.", nameof(reason));

        var value = reason.Trim();
        if (value.Length > MaxReasonLength)
            throw new ArgumentException("A warning reason exceeds 512 characters.", nameof(reason));

        return value;
    }

    public static int ValidateLimit(int limit)
    {
        if (limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(limit));
        return limit;
    }
}

public interface IWarningRepository
{
    ValueTask InsertAsync(WarningRecord warning, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<WarningRecord>> GetActiveAsync(
        PlayerId targetId, DateTimeOffset atUtc, int limit = 100,
        CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<WarningRecord>> GetHistoryAsync(
        PlayerId targetId, int limit = 100, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<WarningRecord>> ClearActiveAsync(
        PlayerId targetId, PlayerId? actorId, string reason, DateTimeOffset atUtc,
        CancellationToken cancellationToken = default);
}

public interface IWarningService
{
    ValueTask<WarningRecord> WarnAsync(
        PlayerId targetId, PlayerId? actorId, string reason, DateTimeOffset atUtc,
        DateTimeOffset? expiresAtUtc = null, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<WarningRecord>> GetActiveAsync(
        PlayerId targetId, DateTimeOffset atUtc, int limit = 100,
        CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<WarningRecord>> GetHistoryAsync(
        PlayerId targetId, int limit = 100, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<WarningRecord>> ClearAsync(
        PlayerId targetId, PlayerId? actorId, string reason, DateTimeOffset atUtc,
        CancellationToken cancellationToken = default);
}
