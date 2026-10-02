using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public interface IKickAnnouncement
{
    ValueTask AnnounceAsync(CancellationToken cancellationToken = default);
}

public sealed class KickCommandExecutor
{
    private static readonly AdminActionId Requested = new("kick.requested");
    private static readonly AdminActionId Completed = new("kick");
    private static readonly AdminActionId SilentRequested = new("kick.silent.requested");
    private static readonly AdminActionId SilentCompleted = new("kick.silent");

    private readonly IModerationTargetGateway _targets;
    private readonly IAdminAuditService _audit;
    private readonly IPlayerDisconnectAction _disconnect;
    private readonly IKickAnnouncement _announcement;
    private readonly TimeProvider _time;

    public KickCommandExecutor(
        IModerationTargetGateway targets,
        IAdminAuditService audit,
        IPlayerDisconnectAction disconnect,
        IKickAnnouncement announcement,
        TimeProvider? time = null)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _disconnect = disconnect ?? throw new ArgumentNullException(nameof(disconnect));
        _announcement = announcement ?? throw new ArgumentNullException(nameof(announcement));
        _time = time ?? TimeProvider.System;
    }

    public static PermissionId GetPermission(bool silent)
        => new(silent ? "ano.admin.silentkick" : "ano.admin.kick");

    public async ValueTask<CommandResult> ExecuteAsync(
        bool silent,
        PlayerId? actor,
        string selector,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        string normalizedReason;
        try
        {
            normalizedReason = AdminAuditValidation.NormalizeReason(
                string.IsNullOrWhiteSpace(reason) ? "No reason provided." : reason);
        }
        catch (ArgumentException exception)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message);
        }

        var resolved = await _targets.ResolveAsync(
            selector, actor, GetPermission(silent), cancellationToken).ConfigureAwait(false);
        if (!resolved.Accepted || resolved.Target is null)
        {
            return CommandResult.Fail(
                resolved.Failure is ModerationTargetFailure.PermissionDenied
                    or ModerationTargetFailure.TargetImmune
                    or ModerationTargetFailure.SelfTargetNotAllowed
                    ? CommandFailureReason.Forbidden : CommandFailureReason.InvalidInput,
                $"Cannot kick target: {resolved.Failure}.");
        }

        var online = resolved.Target.OnlinePlayer;
        if (online is null || !online.IsConnected)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "The target must be online.");
        }

        var now = _time.GetUtcNow();
        try
        {
            await _audit.RecordAsync(
                silent ? SilentRequested : Requested, actor, online.Id,
                normalizedReason, now, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return CommandResult.Fail(CommandFailureReason.HandlerFailed, "The kick could not be audited.");
        }

        try
        {
            await _disconnect.DisconnectAsync(online, normalizedReason, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return CommandResult.Fail(CommandFailureReason.HandlerFailed, "The target could not be disconnected.");
        }

        try
        {
            await _audit.RecordAsync(
                silent ? SilentCompleted : Completed, actor, online.Id,
                normalizedReason, now, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return CommandResult.Fail(
                CommandFailureReason.HandlerFailed,
                "The target was disconnected, but completion could not be audited.");
        }

        if (!silent)
        {
            try
            {
                await _announcement.AnnounceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return CommandResult.Fail(
                    CommandFailureReason.HandlerFailed,
                    "The target was disconnected, but the announcement failed.");
            }
        }

        return CommandResult.Ok($"Kicked {online.Id}.");
    }
}
