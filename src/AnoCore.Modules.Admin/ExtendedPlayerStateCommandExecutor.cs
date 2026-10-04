using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public sealed class ExtendedPlayerStateCommandExecutor
{
    private readonly IModerationTargetGateway _targets;
    private readonly ExtendedPlayerStateService _state;

    public ExtendedPlayerStateCommandExecutor(
        IModerationTargetGateway targets,
        ExtendedPlayerStateService state)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        ExtendedPlayerStateOperation operation,
        PlayerId? actor,
        string selector,
        int? value,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateValue(operation, value, out var normalizedValue);
        if (validation is not null)
        {
            return validation;
        }

        var target = await _targets.ResolveAsync(
            selector,
            actor,
            GetPermission(operation),
            cancellationToken).ConfigureAwait(false);

        if (!target.Accepted || target.Target is null)
        {
            return MapTargetFailure(target.Failure);
        }

        if (target.Target.OnlinePlayer is not { IsConnected: true } player)
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "This command requires a currently connected player.");
        }

        if (RequiresAlive(operation) && !player.IsAlive)
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "This command requires a living player.");
        }

        try
        {
            var changed = await _state.ApplyAsync(
                player,
                new ExtendedPlayerStateMutation(operation, normalizedValue),
                cancellationToken).ConfigureAwait(false);

            return changed
                ? CommandResult.Ok(DescribeSuccess(operation, player, normalizedValue))
                : CommandResult.Ok(
                    $"No AnoCore-owned {DescribeFacet(operation)} state was active for {player.Id}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CommandResult.Fail(
                CommandFailureReason.HandlerFailed,
                "The player state change could not be applied.");
        }
    }

    public static PermissionId GetPermission(ExtendedPlayerStateOperation operation)
        => new(operation switch
        {
            ExtendedPlayerStateOperation.SetHealth => "ano.admin.health",
            ExtendedPlayerStateOperation.SetArmor => "ano.admin.armor",
            ExtendedPlayerStateOperation.Freeze => "ano.admin.freeze",
            ExtendedPlayerStateOperation.Unfreeze => "ano.admin.unfreeze",
            ExtendedPlayerStateOperation.Noclip => "ano.admin.noclip",
            ExtendedPlayerStateOperation.Walk => "ano.admin.walk",
            ExtendedPlayerStateOperation.Slay => "ano.admin.slay",
            ExtendedPlayerStateOperation.SetSpeed => "ano.admin.speed",
            ExtendedPlayerStateOperation.ResetSpeed => "ano.admin.resetspeed",
            ExtendedPlayerStateOperation.Blind => "ano.admin.blind",
            ExtendedPlayerStateOperation.Unblind => "ano.admin.unblind",
            ExtendedPlayerStateOperation.God => "ano.admin.god",
            ExtendedPlayerStateOperation.Ungod => "ano.admin.ungod",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        });

    private static CommandResult? ValidateValue(
        ExtendedPlayerStateOperation operation,
        int? value,
        out int? normalized)
    {
        normalized = value;
        switch (operation)
        {
            case ExtendedPlayerStateOperation.SetHealth:
                return ValidateRange(value, 1, 1000, "Health", out normalized);
            case ExtendedPlayerStateOperation.SetArmor:
                return ValidateRange(value, 0, 1000, "Armor", out normalized);
            case ExtendedPlayerStateOperation.SetSpeed:
                return ValidateRange(value, 25, 400, "Speed percent", out normalized);
            case ExtendedPlayerStateOperation.Blind:
                normalized = value ?? 255;
                return normalized is < 0 or > 255
                    ? CommandResult.Fail(
                        CommandFailureReason.InvalidInput,
                        "Blind alpha must be between 0 and 255.")
                    : null;
            case ExtendedPlayerStateOperation.Freeze:
            case ExtendedPlayerStateOperation.Unfreeze:
            case ExtendedPlayerStateOperation.Noclip:
            case ExtendedPlayerStateOperation.Walk:
            case ExtendedPlayerStateOperation.Slay:
            case ExtendedPlayerStateOperation.ResetSpeed:
            case ExtendedPlayerStateOperation.Unblind:
            case ExtendedPlayerStateOperation.God:
            case ExtendedPlayerStateOperation.Ungod:
                if (value is not null)
                {
                    return CommandResult.Fail(
                        CommandFailureReason.InvalidInput,
                        "This command does not accept a numeric value.");
                }

                normalized = null;
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static CommandResult? ValidateRange(
        int? value,
        int minimum,
        int maximum,
        string label,
        out int? normalized)
    {
        normalized = value;
        if (value is null || value < minimum || value > maximum)
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                $"{label} must be between {minimum} and {maximum}.");
        }

        return null;
    }

    private static bool RequiresAlive(ExtendedPlayerStateOperation operation)
        => operation is
            ExtendedPlayerStateOperation.SetHealth
            or ExtendedPlayerStateOperation.SetArmor
            or ExtendedPlayerStateOperation.Freeze
            or ExtendedPlayerStateOperation.Noclip
            or ExtendedPlayerStateOperation.Slay
            or ExtendedPlayerStateOperation.SetSpeed
            or ExtendedPlayerStateOperation.Blind
            or ExtendedPlayerStateOperation.God;

    private static CommandResult MapTargetFailure(ModerationTargetFailure failure)
        => failure switch
        {
            ModerationTargetFailure.PermissionDenied
                or ModerationTargetFailure.SelfTargetNotAllowed
                or ModerationTargetFailure.TargetImmune
                => CommandResult.Fail(
                    CommandFailureReason.Forbidden,
                    DescribeTargetFailure(failure)),

            ModerationTargetFailure.EmptySelector
                or ModerationTargetFailure.SelectorNotAllowed
                or ModerationTargetFailure.NotFound
                or ModerationTargetFailure.Ambiguous
                or ModerationTargetFailure.ActorNotOnline
                or ModerationTargetFailure.TargetNotOnline
                or ModerationTargetFailure.StaleTarget
                => CommandResult.Fail(
                    CommandFailureReason.InvalidInput,
                    DescribeTargetFailure(failure)),

            _ => CommandResult.Fail(
                CommandFailureReason.HandlerFailed,
                "The target could not be resolved."),
        };

    private static string DescribeTargetFailure(ModerationTargetFailure failure)
        => failure switch
        {
            ModerationTargetFailure.PermissionDenied => "You are not allowed to perform this action.",
            ModerationTargetFailure.SelfTargetNotAllowed => "You cannot target yourself with this action.",
            ModerationTargetFailure.TargetImmune => "The target has equal or higher immunity.",
            ModerationTargetFailure.EmptySelector => "A target is required.",
            ModerationTargetFailure.SelectorNotAllowed => "This command accepts one explicit player target only.",
            ModerationTargetFailure.NotFound => "The target could not be found.",
            ModerationTargetFailure.Ambiguous => "The target name is ambiguous.",
            ModerationTargetFailure.ActorNotOnline => "The acting player is no longer online.",
            ModerationTargetFailure.TargetNotOnline => "The target is no longer online.",
            ModerationTargetFailure.StaleTarget => "The target session changed. Resolve the player again.",
            _ => "The target could not be resolved.",
        };

    private static string DescribeSuccess(
        ExtendedPlayerStateOperation operation,
        PlayerSnapshot player,
        int? value)
        => operation switch
        {
            ExtendedPlayerStateOperation.SetHealth => $"Set {player.Id} health to {value}.",
            ExtendedPlayerStateOperation.SetArmor => $"Set {player.Id} armor to {value}.",
            ExtendedPlayerStateOperation.Freeze => $"Froze {player.Id}.",
            ExtendedPlayerStateOperation.Unfreeze => $"Restored movement for {player.Id}.",
            ExtendedPlayerStateOperation.Noclip => $"Enabled noclip for {player.Id}.",
            ExtendedPlayerStateOperation.Walk => $"Restored normal movement for {player.Id}.",
            ExtendedPlayerStateOperation.Slay => $"Slayed {player.Id}.",
            ExtendedPlayerStateOperation.SetSpeed => $"Set {player.Id} speed to {value}%.",
            ExtendedPlayerStateOperation.ResetSpeed => $"Restored speed for {player.Id}.",
            ExtendedPlayerStateOperation.Blind => $"Blinded {player.Id} at alpha {value}.",
            ExtendedPlayerStateOperation.Unblind => $"Restored vision for {player.Id}.",
            ExtendedPlayerStateOperation.God => $"Enabled god mode for {player.Id}.",
            ExtendedPlayerStateOperation.Ungod => $"Restored damage handling for {player.Id}.",
            _ => $"Updated {player.Id}.",
        };

    private static string DescribeFacet(ExtendedPlayerStateOperation operation)
        => operation switch
        {
            ExtendedPlayerStateOperation.Unfreeze
                or ExtendedPlayerStateOperation.Walk => "movement",
            ExtendedPlayerStateOperation.ResetSpeed => "speed",
            ExtendedPlayerStateOperation.Unblind => "blindness",
            ExtendedPlayerStateOperation.Ungod => "god-mode",
            _ => "player",
        };
}
