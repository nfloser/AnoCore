using System.Globalization;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Targeting;

namespace AnoCore.Modules.Admin;

public sealed class ExtendedPositionCommandExecutor
{
    private readonly IModerationTargetGateway _targets;
    private readonly IPlayerTargetResolver _resolver;
    private readonly ExtendedPositionService _positions;

    public ExtendedPositionCommandExecutor(
        IModerationTargetGateway targets,
        IPlayerTargetResolver resolver,
        ExtendedPositionService positions)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _positions = positions ?? throw new ArgumentNullException(nameof(positions));
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        ExtendedPositionOperation operation,
        PlayerId? actor,
        string selector,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (!TryValidateArguments(
                operation,
                arguments,
                out var coordinates,
                out var destinationSelector,
                out var slapDamage,
                out var validationError))
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                validationError!);
        }

        var resolved = await _targets.ResolveAsync(
            selector,
            actor,
            GetPermission(operation),
            cancellationToken).ConfigureAwait(false);

        if (!resolved.Accepted || resolved.Target?.OnlinePlayer is not { IsConnected: true } target)
        {
            return resolved.Accepted
                ? CommandResult.Fail(
                    CommandFailureReason.InvalidInput,
                    "This command requires a currently connected player.")
                : MapTargetFailure(resolved.Failure);
        }

        if (operation is ExtendedPositionOperation.Respawn or ExtendedPositionOperation.Revive)
        {
            if (target.IsAlive)
            {
                return CommandResult.Fail(
                    CommandFailureReason.InvalidInput,
                    "This command requires a dead player.");
            }
        }
        else if (!target.IsAlive)
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "This command requires a living player.");
        }

        try
        {
            switch (operation)
            {
                case ExtendedPositionOperation.Respawn:
                    await _positions.RespawnAsync(target, cancellationToken)
                        .ConfigureAwait(false);
                    return CommandResult.Ok($"Respawned {target.Id}.");

                case ExtendedPositionOperation.Revive:
                    if (!await _positions.ReviveAsync(target, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        return CommandResult.Fail(
                            CommandFailureReason.InvalidInput,
                            "No death position is available for the current player session.");
                    }

                    return CommandResult.Ok(
                        $"Revived {target.Id} at the recorded death position.");

                case ExtendedPositionOperation.TeleportPosition:
                    await _positions.TeleportAsync(
                        target,
                        coordinates!.Value,
                        cancellationToken).ConfigureAwait(false);
                    return CommandResult.Ok(
                        $"Teleported {target.Id} to {coordinates.Value.X}, "
                        + $"{coordinates.Value.Y}, {coordinates.Value.Z}.");

                case ExtendedPositionOperation.TeleportPlayer:
                    var destination = ResolveDestination(
                        destinationSelector!,
                        actor);
                    if (destination is null)
                    {
                        return CommandResult.Fail(
                            CommandFailureReason.InvalidInput,
                            "The destination player could not be resolved uniquely.");
                    }

                    if (!destination.IsConnected || !destination.IsAlive)
                    {
                        return CommandResult.Fail(
                            CommandFailureReason.InvalidInput,
                            "The destination player must be alive and connected.");
                    }

                    await _positions.TeleportToAsync(
                        target,
                        destination,
                        cancellationToken).ConfigureAwait(false);
                    return CommandResult.Ok(
                        $"Teleported {target.Id} to {destination.Id}.");

                case ExtendedPositionOperation.Bury:
                    await _positions.MoveVerticalAsync(
                        target,
                        -25f,
                        cancellationToken).ConfigureAwait(false);
                    return CommandResult.Ok($"Buried {target.Id}.");

                case ExtendedPositionOperation.Unbury:
                    await _positions.MoveVerticalAsync(
                        target,
                        30f,
                        cancellationToken).ConfigureAwait(false);
                    return CommandResult.Ok($"Unburied {target.Id}.");

                case ExtendedPositionOperation.Slap:
                    await _positions.SlapAsync(
                        target,
                        slapDamage,
                        cancellationToken).ConfigureAwait(false);
                    return CommandResult.Ok(
                        $"Slapped {target.Id} for {slapDamage} damage.");

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(operation),
                        operation,
                        null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CommandResult.Fail(
                CommandFailureReason.HandlerFailed,
                "The positional player action could not be completed.");
        }
    }

    public static PermissionId GetPermission(ExtendedPositionOperation operation)
        => new(operation switch
        {
            ExtendedPositionOperation.Respawn => "ano.admin.respawn",
            ExtendedPositionOperation.Revive => "ano.admin.revive",
            ExtendedPositionOperation.TeleportPosition
                or ExtendedPositionOperation.TeleportPlayer => "ano.admin.teleport",
            ExtendedPositionOperation.Bury
                or ExtendedPositionOperation.Unbury => "ano.admin.bury",
            ExtendedPositionOperation.Slap => "ano.admin.slap",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        });

    private PlayerSnapshot? ResolveDestination(
        string selector,
        PlayerId? actor)
    {
        var result = _resolver.Resolve(
            selector,
            actor,
            TargetSelectorCapabilities.Self);

        return result.Accepted && result.Targets.Count == 1
            ? result.Targets[0]
            : null;
    }

    private static bool TryValidateArguments(
        ExtendedPositionOperation operation,
        IReadOnlyList<string> arguments,
        out PlayerWorldPosition? coordinates,
        out string? destinationSelector,
        out int slapDamage,
        out string? error)
    {
        coordinates = null;
        destinationSelector = null;
        slapDamage = 0;
        error = null;

        switch (operation)
        {
            case ExtendedPositionOperation.Respawn:
            case ExtendedPositionOperation.Revive:
            case ExtendedPositionOperation.Bury:
            case ExtendedPositionOperation.Unbury:
                if (arguments.Count != 0)
                {
                    error = "This command does not accept additional arguments.";
                    return false;
                }

                return true;

            case ExtendedPositionOperation.TeleportPosition:
                if (arguments.Count != 3
                    || !TryParseCoordinate(arguments[0], out var x)
                    || !TryParseCoordinate(arguments[1], out var y)
                    || !TryParseCoordinate(arguments[2], out var z))
                {
                    error = "Coordinates must be finite numbers within the supported world range.";
                    return false;
                }

                try
                {
                    coordinates = new PlayerWorldPosition(x, y, z);
                    return true;
                }
                catch (ArgumentOutOfRangeException)
                {
                    error = "Coordinates must be finite numbers within the supported world range.";
                    return false;
                }

            case ExtendedPositionOperation.TeleportPlayer:
                if (arguments.Count != 1 || string.IsNullOrWhiteSpace(arguments[0]))
                {
                    error = "A destination player is required.";
                    return false;
                }

                destinationSelector = arguments[0].Trim();
                return true;

            case ExtendedPositionOperation.Slap:
                if (arguments.Count > 1)
                {
                    error = "Slap accepts at most one damage value.";
                    return false;
                }

                if (arguments.Count == 0)
                {
                    slapDamage = 0;
                    return true;
                }

                if (!int.TryParse(
                        arguments[0],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out slapDamage)
                    || slapDamage is < 0 or > 1000)
                {
                    error = "Slap damage must be between 0 and 1000.";
                    return false;
                }

                return true;

            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static bool TryParseCoordinate(string raw, out float value)
        => float.TryParse(
               raw,
               NumberStyles.Float,
               CultureInfo.InvariantCulture,
               out value)
           && float.IsFinite(value)
           && Math.Abs(value) <= PlayerWorldPosition.CoordinateLimit;

    private static CommandResult MapTargetFailure(
        ModerationTargetFailure failure)
        => failure switch
        {
            ModerationTargetFailure.PermissionDenied
                or ModerationTargetFailure.SelfTargetNotAllowed
                or ModerationTargetFailure.TargetImmune
                => CommandResult.Fail(
                    CommandFailureReason.Forbidden,
                    "The target is not authorized for this action."),

            ModerationTargetFailure.EmptySelector
                or ModerationTargetFailure.SelectorNotAllowed
                or ModerationTargetFailure.NotFound
                or ModerationTargetFailure.Ambiguous
                or ModerationTargetFailure.ActorNotOnline
                or ModerationTargetFailure.TargetNotOnline
                or ModerationTargetFailure.StaleTarget
                => CommandResult.Fail(
                    CommandFailureReason.InvalidInput,
                    "The target could not be resolved as a current online player."),

            _ => CommandResult.Fail(
                CommandFailureReason.HandlerFailed,
                "The target could not be resolved."),
        };
}
