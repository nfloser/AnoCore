using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Admin;

public sealed class StatisticsResetCommandExecutor
{
    public static readonly PermissionId Permission = new("ano.stats.reset");
    public const string DefaultReason = "No reason provided.";

    private readonly IModerationTargetGateway _targets;
    private readonly IStatisticsResetAdministrationService _administration;
    private readonly TimeProvider _timeProvider;

    public StatisticsResetCommandExecutor(
        IModerationTargetGateway targets,
        IStatisticsResetAdministrationService administration,
        TimeProvider? timeProvider = null)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _administration = administration ?? throw new ArgumentNullException(nameof(administration));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        PlayerId? actor,
        string selector,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var target = await _targets.ResolveAsync(
            selector, actor, Permission, cancellationToken).ConfigureAwait(false);
        if (!target.Accepted || target.Target is null)
            return MapTargetFailure(target.Failure);

        try
        {
            var result = await _administration.ResetAsync(
                target.Target.Id,
                actor,
                string.IsNullOrWhiteSpace(reason) ? DefaultReason : reason,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            return CommandResult.Ok(
                $"Statistics reset for {target.Target.Id} at {result.CurrentCutoffUtc:O}.");
        }
        catch (ArgumentException exception)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, exception.Message);
        }
    }

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
                "The statistics reset target could not be resolved."),
        };

    private static string Describe(ModerationTargetFailure failure)
        => failure switch
        {
            ModerationTargetFailure.EmptySelector => "A target is required.",
            ModerationTargetFailure.SelectorNotAllowed =>
                "This command accepts one explicit player target only.",
            ModerationTargetFailure.NotFound =>
                "The target could not be found. Offline targets require an explicit SteamID64.",
            ModerationTargetFailure.Ambiguous => "The target is ambiguous.",
            ModerationTargetFailure.ActorNotOnline =>
                "The calling player is no longer connected.",
            ModerationTargetFailure.PermissionDenied =>
                $"Permission '{Permission}' is required.",
            ModerationTargetFailure.SelfTargetNotAllowed =>
                "You cannot reset your own statistics through the administrative command.",
            ModerationTargetFailure.TargetNotOnline => "The target is no longer connected.",
            ModerationTargetFailure.StaleTarget => "The target session changed.",
            ModerationTargetFailure.TargetImmune => "The target is protected by immunity.",
            _ => "The statistics reset target could not be resolved.",
        };
}
