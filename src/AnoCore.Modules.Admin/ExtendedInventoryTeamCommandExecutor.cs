using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public sealed class ExtendedInventoryTeamCommandExecutor
{
    private static readonly PermissionId HidePermission = new("ano.admin.hide");
    private readonly IModerationTargetGateway _targets;
    private readonly IPlayerRegistry _players;
    private readonly IPermissionEvaluator _permissions;
    private readonly IExtendedInventoryTeamTransport _transport;

    public ExtendedInventoryTeamCommandExecutor(
        IModerationTargetGateway targets,
        IPlayerRegistry players,
        IPermissionEvaluator permissions,
        IExtendedInventoryTeamTransport transport)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async ValueTask<CommandResult> ExecuteTargetAsync(
        ExtendedInventoryTeamOperation operation,
        PlayerId? actor,
        string selector,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (!TryValidate(
                operation,
                arguments,
                out var name,
                out var item,
                out var team,
                out var error))
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, error!);
        }

        var resolved = await _targets.ResolveAsync(
            selector,
            actor,
            GetPermission(operation),
            cancellationToken).ConfigureAwait(false);

        if (!resolved.Accepted || resolved.Target?.OnlinePlayer is not { IsConnected: true } player)
        {
            return resolved.Accepted
                ? CommandResult.Fail(
                    CommandFailureReason.InvalidInput,
                    "This command requires a currently connected player.")
                : MapTargetFailure(resolved.Failure);
        }

        if (operation is ExtendedInventoryTeamOperation.Strip
            or ExtendedInventoryTeamOperation.Give
            && !player.IsAlive)
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "This command requires a living player.");
        }

        try
        {
            switch (operation)
            {
                case ExtendedInventoryTeamOperation.Rename:
                    await _transport.RenameAsync(
                        player,
                        name!,
                        cancellationToken).ConfigureAwait(false);
                    return CommandResult.Ok($"Renamed {player.Id}.");

                case ExtendedInventoryTeamOperation.Strip:
                    await _transport.StripAsync(
                        player,
                        cancellationToken).ConfigureAwait(false);
                    return CommandResult.Ok($"Stripped {player.Id}'s inventory.");

                case ExtendedInventoryTeamOperation.Give:
                    if (!await _transport.GiveAsync(
                            player,
                            item!,
                            cancellationToken).ConfigureAwait(false))
                    {
                        return CommandResult.Fail(
                            CommandFailureReason.InvalidInput,
                            "The item could not be added because the relevant inventory limit was reached.");
                    }

                    return CommandResult.Ok(
                        $"Gave {item!.DisplayName} to {player.Id}.");

                case ExtendedInventoryTeamOperation.SetTeam:
                    await _transport.SetTeamAsync(
                        player,
                        team!.Value,
                        cancellationToken).ConfigureAwait(false);
                    return CommandResult.Ok(
                        $"Moved {player.Id} to {team.Value}.");

                case ExtendedInventoryTeamOperation.SwapTeam:
                    var targetTeam = player.Team switch
                    {
                        PlayerTeam.Terrorist => PlayerTeam.CounterTerrorist,
                        PlayerTeam.CounterTerrorist => PlayerTeam.Terrorist,
                        _ => PlayerTeam.Unknown,
                    };

                    if (targetTeam == PlayerTeam.Unknown)
                    {
                        return CommandResult.Fail(
                            CommandFailureReason.InvalidInput,
                            "Only Terrorist and Counter-Terrorist players can be swapped.");
                    }

                    await _transport.SetTeamAsync(
                        player,
                        targetTeam,
                        cancellationToken).ConfigureAwait(false);
                    return CommandResult.Ok(
                        $"Swapped {player.Id} to {targetTeam}.");

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
                "The player administration action could not be completed.");
        }
    }

    public async ValueTask<CommandResult> ExecuteHideAsync(
        PlayerId? actor,
        CancellationToken cancellationToken = default)
    {
        if (actor is null)
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "Hide is available only to a connected player.");
        }

        if (!await _permissions.HasPermissionAsync(
                actor,
                HidePermission,
                cancellationToken).ConfigureAwait(false))
        {
            return CommandResult.Fail(
                CommandFailureReason.Forbidden,
                "You are not allowed to hide your player session.");
        }

        if (!_players.TryGet(actor, out var player)
            || player is null
            || !player.IsConnected)
        {
            return CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "The acting player is no longer connected.");
        }

        try
        {
            await _transport.HideAsync(player, cancellationToken)
                .ConfigureAwait(false);
            return CommandResult.Ok("Your player session is now hidden.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CommandResult.Fail(
                CommandFailureReason.HandlerFailed,
                "The hide action could not be completed.");
        }
    }

    public static PermissionId GetPermission(
        ExtendedInventoryTeamOperation operation)
        => new(operation switch
        {
            ExtendedInventoryTeamOperation.Rename => "ano.admin.rename",
            ExtendedInventoryTeamOperation.Strip => "ano.admin.strip",
            ExtendedInventoryTeamOperation.Give => "ano.admin.give",
            ExtendedInventoryTeamOperation.SetTeam => "ano.admin.team",
            ExtendedInventoryTeamOperation.SwapTeam => "ano.admin.swap",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        });

    public static PermissionId GetHidePermission() => HidePermission;

    private static bool TryValidate(
        ExtendedInventoryTeamOperation operation,
        IReadOnlyList<string> arguments,
        out string? name,
        out AdminItemDefinition? item,
        out PlayerTeam? team,
        out string? error)
    {
        name = null;
        item = null;
        team = null;
        error = null;

        switch (operation)
        {
            case ExtendedInventoryTeamOperation.Rename:
                if (arguments.Count != 1
                    || !TryNormalizeName(arguments[0], out name))
                {
                    error = "A printable player name from 1 to 64 characters is required.";
                    return false;
                }

                return true;

            case ExtendedInventoryTeamOperation.Strip:
            case ExtendedInventoryTeamOperation.SwapTeam:
                if (arguments.Count != 0)
                {
                    error = "This command does not accept additional arguments.";
                    return false;
                }

                return true;

            case ExtendedInventoryTeamOperation.Give:
                if (arguments.Count != 1
                    || !AdminItemCatalog.TryResolve(arguments[0], out item)
                    || item is null)
                {
                    error = "The requested item is not in the approved administration item catalog.";
                    return false;
                }

                return true;

            case ExtendedInventoryTeamOperation.SetTeam:
                if (arguments.Count != 1
                    || !TryParseTeam(arguments[0], out var parsedTeam))
                {
                    error = "Team must be t, ct or spec.";
                    return false;
                }

                team = parsedTeam;
                return true;

            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static bool TryNormalizeName(
        string raw,
        out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var value = raw.Trim();
        if (value.Length is < 1 or > 64
            || value.Any(char.IsControl))
        {
            return false;
        }

        normalized = value;
        return true;
    }

    private static bool TryParseTeam(
        string raw,
        out PlayerTeam team)
    {
        team = raw.Trim().ToLowerInvariant() switch
        {
            "t" or "tt" or "terrorist" => PlayerTeam.Terrorist,
            "ct" or "counterterrorist" or "counter-terrorist" => PlayerTeam.CounterTerrorist,
            "spec" or "spectator" => PlayerTeam.Spectator,
            _ => PlayerTeam.Unknown,
        };

        return team != PlayerTeam.Unknown;
    }

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
