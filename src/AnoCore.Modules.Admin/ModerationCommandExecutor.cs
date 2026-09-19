using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public enum ModerationAdminOperation
{
    Ban = 1,
    Unban = 2,
    Mute = 3,
    Unmute = 4,
    Gag = 5,
    Ungag = 6,
    Silence = 7,
    Unsilence = 8,
}

public sealed class ModerationCommandExecutor
{
    public const string DefaultReason = "No reason provided.";

    private readonly IModerationTargetGateway _targets;
    private readonly IModerationService _moderation;
    private readonly TimeProvider _timeProvider;

    public ModerationCommandExecutor(
        IModerationTargetGateway targets,
        IModerationService moderation,
        TimeProvider? timeProvider = null)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _moderation = moderation ?? throw new ArgumentNullException(nameof(moderation));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        ModerationAdminOperation operation,
        PlayerId? actor,
        string selector,
        int? durationMinutes,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var definition = GetDefinition(operation);
        var now = _timeProvider.GetUtcNow().ToUniversalTime();

        DateTimeOffset? expiresAt = null;
        if (!definition.IsRevoke)
        {
            if (durationMinutes is null || durationMinutes < 0)
            {
                return CommandResult.Fail(
                    CommandFailureReason.InvalidInput,
                    "Duration must be zero for permanent or a positive number of minutes.");
            }

            if (durationMinutes > 0)
            {
                try
                {
                    expiresAt = now.AddMinutes(durationMinutes.Value);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return CommandResult.Fail(
                        CommandFailureReason.InvalidInput,
                        "Duration exceeds the supported timestamp range.");
                }
            }
        }
        else if (durationMinutes is not null)
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "Revoke operations do not accept a duration.");
        }

        string normalizedReason;
        try
        {
            normalizedReason = ModerationValidation.NormalizeReason(
                string.IsNullOrWhiteSpace(reason) ? DefaultReason : reason);
        }
        catch (ArgumentException exception)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message);
        }

        var target = await _targets.ResolveAsync(
            selector,
            actor,
            definition.Permission,
            cancellationToken).ConfigureAwait(false);

        if (!target.Accepted || target.Target is null)
        {
            return MapTargetFailure(target.Failure);
        }

        if (definition.IsRevoke)
        {
            var revoked = await _moderation.RevokeAsync(
                target.Target.Id,
                actor,
                definition.Restrictions,
                normalizedReason,
                now,
                cancellationToken).ConfigureAwait(false);

            return revoked.Count == 0
                ? CommandResult.Fail(
                    CommandFailureReason.InvalidInput,
                    $"No active {definition.Label} restriction exists for the target.")
                : CommandResult.Ok($"{definition.SuccessVerb} {definition.Label} restriction for {target.Target.Id}.");
        }

        await _moderation.ApplyAsync(
            target.Target.Id,
            actor,
            definition.Restrictions,
            normalizedReason,
            now,
            expiresAt,
            cancellationToken).ConfigureAwait(false);

        return CommandResult.Ok(
            durationMinutes == 0
                ? $"Applied permanent {definition.Label} restriction to {target.Target.Id}."
                : $"Applied {definition.Label} restriction to {target.Target.Id} for {durationMinutes} minute(s).");
    }

    public static PermissionId GetPermission(ModerationAdminOperation operation)
        => GetDefinition(operation).Permission;

    private static CommandResult MapTargetFailure(ModerationTargetFailure failure)
        => failure switch
        {
            ModerationTargetFailure.PermissionDenied
                or ModerationTargetFailure.SelfTargetNotAllowed
                or ModerationTargetFailure.TargetImmune
                => CommandResult.Fail(CommandFailureReason.Forbidden, DescribeTargetFailure(failure)),

            ModerationTargetFailure.EmptySelector
                or ModerationTargetFailure.SelectorNotAllowed
                or ModerationTargetFailure.NotFound
                or ModerationTargetFailure.Ambiguous
                or ModerationTargetFailure.ActorNotOnline
                or ModerationTargetFailure.TargetNotOnline
                or ModerationTargetFailure.StaleTarget
                => CommandResult.Fail(CommandFailureReason.InvalidInput, DescribeTargetFailure(failure)),

            ModerationTargetFailure.None
                => CommandResult.Fail(CommandFailureReason.HandlerFailed, "Target resolution returned no target."),

            _ => CommandResult.Fail(CommandFailureReason.HandlerFailed, $"Unhandled target failure: {failure}."),
        };

    private static string DescribeTargetFailure(ModerationTargetFailure failure)
        => failure switch
        {
            ModerationTargetFailure.EmptySelector => "A target is required.",
            ModerationTargetFailure.SelectorNotAllowed => "This command accepts one explicit player target only.",
            ModerationTargetFailure.NotFound => "The target could not be found. Offline targets require an explicit SteamID64.",
            ModerationTargetFailure.Ambiguous => "The target name is ambiguous.",
            ModerationTargetFailure.ActorNotOnline => "The acting player is no longer online.",
            ModerationTargetFailure.PermissionDenied => "You are not allowed to perform this moderation action.",
            ModerationTargetFailure.SelfTargetNotAllowed => "You cannot target yourself with this moderation action.",
            ModerationTargetFailure.TargetNotOnline => "The target is no longer online.",
            ModerationTargetFailure.StaleTarget => "The target session changed. Resolve the player again.",
            ModerationTargetFailure.TargetImmune => "The target has equal or higher immunity.",
            _ => "The moderation target could not be resolved.",
        };

    private static OperationDefinition GetDefinition(ModerationAdminOperation operation)
        => operation switch
        {
            ModerationAdminOperation.Ban => new(
                new PermissionId("ano.admin.ban"),
                ModerationRestriction.Connect,
                false,
                "ban",
                "Cleared"),
            ModerationAdminOperation.Unban => new(
                new PermissionId("ano.admin.unban"),
                ModerationRestriction.Connect,
                true,
                "ban",
                "Cleared"),
            ModerationAdminOperation.Mute => new(
                new PermissionId("ano.admin.mute"),
                ModerationRestriction.Voice,
                false,
                "mute",
                "Cleared"),
            ModerationAdminOperation.Unmute => new(
                new PermissionId("ano.admin.unmute"),
                ModerationRestriction.Voice,
                true,
                "mute",
                "Cleared"),
            ModerationAdminOperation.Gag => new(
                new PermissionId("ano.admin.gag"),
                ModerationRestriction.Chat,
                false,
                "gag",
                "Cleared"),
            ModerationAdminOperation.Ungag => new(
                new PermissionId("ano.admin.ungag"),
                ModerationRestriction.Chat,
                true,
                "gag",
                "Cleared"),
            ModerationAdminOperation.Silence => new(
                new PermissionId("ano.admin.silence"),
                ModerationRestriction.Voice | ModerationRestriction.Chat,
                false,
                "silence",
                "Cleared"),
            ModerationAdminOperation.Unsilence => new(
                new PermissionId("ano.admin.unsilence"),
                ModerationRestriction.Voice | ModerationRestriction.Chat,
                true,
                "silence",
                "Cleared"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };

    private sealed record OperationDefinition(
        PermissionId Permission,
        ModerationRestriction Restrictions,
        bool IsRevoke,
        string Label,
        string SuccessVerb);
}
