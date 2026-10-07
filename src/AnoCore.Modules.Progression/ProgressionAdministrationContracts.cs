using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression;

public enum ProgressionAdminOperation
{
    Give,
    Take,
    Set,
    Reset,
}

public sealed record ProgressionAdminRequest(Guid RequestId, ProgressionAdminOperation Operation,
    PlayerId Target, long Amount, PlayerId? Actor, string Reason, DateTimeOffset OccurredAtUtc);

public sealed record ProgressionAdminResult(bool Applied, Guid RequestId, long PreviousXp, long CurrentXp, long Revision);

public interface IProgressionAdministrationService
{
    ValueTask<ProgressionAdminResult> ApplyAsync(ProgressionAdminRequest request, CancellationToken cancellationToken = default);
}

public static class ProgressionAdministrationPolicy
{
    public const long MaximumAmount = 1_000_000_000;
    public const int MaximumReasonLength = 384;

    public static ProgressionAdminRequest Validate(ProgressionAdminRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Target);
        if (request.RequestId == Guid.Empty || !Enum.IsDefined(request.Operation)
            || request.Amount is < 0 or > MaximumAmount
            || request.Operation is ProgressionAdminOperation.Give or ProgressionAdminOperation.Take && request.Amount == 0
            || request.Operation == ProgressionAdminOperation.Reset && request.Amount != 0
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > MaximumReasonLength
            || request.Reason != request.Reason.Trim() || request.Reason.Any(char.IsControl))
            throw new ArgumentException("XP administration requires a request ID, bounded amount and printable reason.");
        var utc = request.OccurredAtUtc.ToUniversalTime();
        return request with { OccurredAtUtc = new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero) };
    }

    public static long Calculate(ProgressionAdminOperation operation, long previous, long amount)
    {
        if (previous < 0) throw new ArgumentOutOfRangeException(nameof(previous));
        long current;
        try
        {
            current = operation switch
            {
                ProgressionAdminOperation.Give => checked(previous + amount),
                ProgressionAdminOperation.Take => checked(previous - amount),
                ProgressionAdminOperation.Set => amount,
                ProgressionAdminOperation.Reset => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            };
        }
        catch (OverflowException) { throw new ArgumentOutOfRangeException(nameof(amount), "Resulting XP exceeds the supported range."); }
        if (current < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Lifetime XP cannot become negative.");
        return current;
    }
}
