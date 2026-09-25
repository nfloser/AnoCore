using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Warnings;

namespace AnoCore.Modules.Admin;

public sealed class WarningCommandExecutor
{
    public static readonly PermissionId WarnPermission = new("ano.admin.warn");
    public static readonly PermissionId ReadPermission = new("ano.admin.warns");
    public static readonly PermissionId ClearPermission = new("ano.admin.clearwarns");
    private static readonly AdminActionId WarnRequested = new("warning.requested");
    private static readonly AdminActionId WarnCompleted = new("warning.completed");
    private static readonly AdminActionId ClearRequested = new("warning.clear.requested");
    private static readonly AdminActionId ClearCompleted = new("warning.clear.completed");
    private const string DefaultReason = "No reason provided.";
    private readonly IModerationTargetGateway _targets;
    private readonly IWarningService _warnings;
    private readonly IAdminAuditService _audit;
    private readonly TimeProvider _clock;

    public WarningCommandExecutor(IModerationTargetGateway targets, IWarningService warnings,
        IAdminAuditService audit, TimeProvider? clock = null)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? TimeProvider.System;
    }

    public async ValueTask<CommandResult> WarnAsync(PlayerId? actor, string selector, int minutes,
        string? reason, CancellationToken cancellationToken = default)
    {
        if (minutes < 0)
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "Duration must be zero or positive.");
        var now = _clock.GetUtcNow();
        DateTimeOffset? expires;
        try
        {
            expires = minutes == 0 ? null : now.AddMinutes(minutes);
        }
        catch (ArgumentOutOfRangeException)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "Duration exceeds the supported range.");
        }
        var validReason = ValidateReason(reason);
        if (validReason.Error is not null) return validReason.Error;
        var target = await ResolveAsync(selector, actor, WarnPermission, true, cancellationToken)
            .ConfigureAwait(false);
        if (target.Error is not null) return target.Error;
        await _audit.RecordAsync(WarnRequested, actor, target.Id, validReason.Value!, now,
            cancellationToken).ConfigureAwait(false);
        await _warnings.WarnAsync(target.Id!, actor, validReason.Value!, now, expires,
            cancellationToken).ConfigureAwait(false);
        await _audit.RecordAsync(WarnCompleted, actor, target.Id, validReason.Value!, now,
            cancellationToken).ConfigureAwait(false);
        return CommandResult.Ok($"Warned {target.Id}; {(minutes == 0 ? "permanent" : $"{minutes} minute(s)")}.");
    }

    public async ValueTask<CommandResult> ClearAsync(PlayerId? actor, string selector, string? reason,
        CancellationToken cancellationToken = default)
    {
        var validReason = ValidateReason(reason);
        if (validReason.Error is not null) return validReason.Error;
        var target = await ResolveAsync(selector, actor, ClearPermission, true, cancellationToken)
            .ConfigureAwait(false);
        if (target.Error is not null) return target.Error;
        var now = _clock.GetUtcNow();
        await _audit.RecordAsync(ClearRequested, actor, target.Id, validReason.Value!, now,
            cancellationToken).ConfigureAwait(false);
        var cleared = await _warnings.ClearAsync(target.Id!, actor, validReason.Value!, now,
            cancellationToken).ConfigureAwait(false);
        await _audit.RecordAsync(ClearCompleted, actor, target.Id, validReason.Value!, now,
            cancellationToken).ConfigureAwait(false);
        return cleared.Count == 0
            ? CommandResult.Fail(CommandFailureReason.InvalidInput, "No active warnings exist for this player.")
            : CommandResult.Ok($"Cleared {cleared.Count} warning(s) for {target.Id}.");
    }

    public async ValueTask<CommandResult> OwnHistoryAsync(PlayerId? actor,
        CancellationToken cancellationToken = default)
    {
        if (actor is null)
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "A player is required.");
        return Format(await _warnings.GetHistoryAsync(actor, 10, cancellationToken)
            .ConfigureAwait(false), _clock.GetUtcNow());
    }

    public async ValueTask<CommandResult> TargetHistoryAsync(PlayerId? actor, string selector,
        CancellationToken cancellationToken = default)
    {
        var target = await ResolveAsync(selector, actor, ReadPermission, false, cancellationToken)
            .ConfigureAwait(false);
        if (target.Error is not null) return target.Error;
        return Format(await _warnings.GetHistoryAsync(target.Id!, 10, cancellationToken)
            .ConfigureAwait(false), _clock.GetUtcNow());
    }

    private async ValueTask<(PlayerId? Id, CommandResult? Error)> ResolveAsync(string selector,
        PlayerId? actor, PermissionId permission, bool requireOnline, CancellationToken cancellationToken)
    {
        var result = await _targets.ResolveAsync(selector, actor, permission, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Accepted || result.Target is null)
        {
            var forbidden = result.Failure is ModerationTargetFailure.PermissionDenied
                or ModerationTargetFailure.TargetImmune or ModerationTargetFailure.SelfTargetNotAllowed;
            return (null, CommandResult.Fail(forbidden ? CommandFailureReason.Forbidden
                : CommandFailureReason.InvalidInput, $"Target unavailable: {result.Failure}."));
        }
        if (requireOnline && (result.Target.OnlinePlayer is null || !result.Target.OnlinePlayer.IsConnected))
            return (null, CommandResult.Fail(CommandFailureReason.InvalidInput, "The target must be online."));
        return (result.Target.Id, null);
    }

    private static (string? Value, CommandResult? Error) ValidateReason(string? reason)
    {
        try
        {
            return (WarningValidation.NormalizeReason(
                string.IsNullOrWhiteSpace(reason) ? DefaultReason : reason), null);
        }
        catch (ArgumentException error)
        {
            return (null, CommandResult.Fail(CommandFailureReason.InvalidInput, error.Message));
        }
    }

    private static CommandResult Format(IReadOnlyList<WarningRecord> warnings, DateTimeOffset now)
    {
        if (warnings.Count == 0) return CommandResult.Ok("No warnings recorded.");
        return CommandResult.Ok(string.Join("\n", warnings.Select(warning =>
            $"{warning.CreatedAtUtc:yyyy-MM-dd HH:mm} UTC: {warning.Reason} "
            + $"({(warning.IsActiveAt(now) ? "active" : warning.ClearedAtUtc is not null ? "cleared" : "expired")})")));
    }
}
