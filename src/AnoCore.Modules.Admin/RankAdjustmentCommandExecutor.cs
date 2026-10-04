using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Admin;

public sealed class RankAdjustmentCommandExecutor
{
    public const string DefaultReason = "No reason provided.";

    private readonly IModerationTargetGateway _targets;
    private readonly IRankAdjustmentAdministrationService _administration;
    private readonly TimeProvider _timeProvider;

    public RankAdjustmentCommandExecutor(
        IModerationTargetGateway targets,
        IRankAdjustmentAdministrationService administration,
        TimeProvider? timeProvider = null)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _administration = administration
            ?? throw new ArgumentNullException(nameof(administration));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        RankAdjustmentAdminOperation operation,
        PlayerId? actor,
        string selector,
        long points,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var permission = GetPermission(operation);
        var target = await _targets.ResolveAsync(
            selector, actor, permission, cancellationToken).ConfigureAwait(false);
        if (!target.Accepted || target.Target is null)
            return MapTargetFailure(target.Failure);

        try
        {
            var result = await _administration.ApplyAsync(
                operation,
                target.Target.Id,
                points,
                actor,
                string.IsNullOrWhiteSpace(reason) ? DefaultReason : reason,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            return CommandResult.Ok(
                $"Rank point adjustment for {target.Target.Id}: "
                + $"{result.PreviousPoints} -> {result.CurrentPoints}.");
        }
        catch (ArgumentException exception)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message);
        }
    }

    public static PermissionId GetPermission(RankAdjustmentAdminOperation operation)
        => operation switch
        {
            RankAdjustmentAdminOperation.Give => new("ano.ranks.points.give"),
            RankAdjustmentAdminOperation.Take => new("ano.ranks.points.take"),
            RankAdjustmentAdminOperation.Set => new("ano.ranks.points.set"),
            RankAdjustmentAdminOperation.Reset => new("ano.ranks.points.reset"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };

    private static CommandResult MapTargetFailure(ModerationTargetFailure failure)
        => failure switch
        {
            ModerationTargetFailure.PermissionDenied
                or ModerationTargetFailure.SelfTargetNotAllowed
                or ModerationTargetFailure.TargetImmune
                => CommandResult.Fail(CommandFailureReason.Forbidden, Describe(failure)),
            ModerationTargetFailure.EmptySelector
                or ModerationTargetFailure.SelectorNotAllowed
                or ModerationTargetFailure.NotFound
                or ModerationTargetFailure.Ambiguous
                or ModerationTargetFailure.ActorNotOnline
                or ModerationTargetFailure.TargetNotOnline
                or ModerationTargetFailure.StaleTarget
                => CommandResult.Fail(CommandFailureReason.InvalidInput, Describe(failure)),
            _ => CommandResult.Fail(CommandFailureReason.HandlerFailed,
                "The rank adjustment target could not be resolved."),
        };

    private static string Describe(ModerationTargetFailure failure)
        => failure switch
        {
            ModerationTargetFailure.EmptySelector => "A target is required.",
            ModerationTargetFailure.SelectorNotAllowed =>
                "This command accepts one explicit player target only.",
            ModerationTargetFailure.NotFound =>
                "The target could not be found. Offline targets require an explicit SteamID64.",
            ModerationTargetFailure.Ambiguous => "The target name is ambiguous.",
            ModerationTargetFailure.ActorNotOnline => "The acting player is no longer online.",
            ModerationTargetFailure.PermissionDenied =>
                "You are not allowed to change rank points.",
            ModerationTargetFailure.SelfTargetNotAllowed =>
                "You cannot change your own rank points.",
            ModerationTargetFailure.TargetNotOnline => "The target is no longer online.",
            ModerationTargetFailure.StaleTarget =>
                "The target session changed. Resolve the player again.",
            ModerationTargetFailure.TargetImmune =>
                "The target has equal or higher immunity.",
            _ => "The rank adjustment target could not be resolved.",
        };
}
