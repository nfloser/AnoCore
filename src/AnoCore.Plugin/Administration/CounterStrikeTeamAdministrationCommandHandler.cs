using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace AnoCore.Plugin.Administration;

public sealed class CounterStrikeTeamAdministrationCommandHandler(
    IPlayerRegistry players, ILogger logger, Func<bool> isActive)
    : ITeamAdministrationCommandHandler
{
    private int _disposed;

    public ValueTask<CommandResult> ExecuteAsync(ExtendedInventoryTeamOperation operation,
        PlayerId? actor, string selector, string? requestedTeam, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlayerSnapshot? actorSession = null;
        if (actor is not null && (!players.TryGet(actor, out actorSession) || actorSession?.IsConnected != true))
        {
            return ValueTask.FromResult(Unavailable());
        }

        var completion = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue(() =>
        {
            var caller = Resolve(actorSession);
            if (actorSession is not null && caller is null)
            {
                return Unavailable();
            }

            var denied = CssTeamAdministrationPolicy.Authorize(caller is null
                || AdminManager.PlayerHasPermissions(caller, CssTeamAdministrationPolicy.RequiredFlag));
            if (denied is not null)
            {
                return denied;
            }

            var targets = new Target(selector).GetTarget(caller).Players
                .Where(IsHuman).DistinctBy(value => value.SteamID).ToArray();
            denied = CssTeamAdministrationPolicy.CheckTarget(targets.Length,
                targets.Length == 1 && (caller is null || AdminManager.CanPlayerTarget(caller, targets[0])));
            if (denied is not null)
            {
                return denied;
            }

            var target = targets[0];
            if (!players.TryGet(new PlayerId(target.SteamID), out var targetSession)
                || targetSession?.IsConnected != true)
            {
                return Unavailable();
            }

            if (!CssTeamAdministrationPolicy.TryDestination(operation, requestedTeam,
                (PlayerTeam)target.TeamNum, out var destination))
            {
                return CommandResult.Fail(CommandFailureReason.InvalidInput,
                    "Use t, ct or spec; only players currently on T or CT can be swapped.");
            }

            CommandResult Move()
            {
                var currentCaller = Resolve(actorSession);
                var currentTarget = Resolve(targetSession);
                if (currentTarget is null || (actorSession is not null && currentCaller is null))
                {
                    return Unavailable();
                }

                var failure = CssTeamAdministrationPolicy.Authorize(currentCaller is null
                    || AdminManager.PlayerHasPermissions(currentCaller, CssTeamAdministrationPolicy.RequiredFlag))
                    ?? CssTeamAdministrationPolicy.CheckTarget(1,
                        currentCaller is null || AdminManager.CanPlayerTarget(currentCaller, currentTarget));
                if (failure is not null)
                {
                    return failure;
                }

                if (destination == PlayerTeam.Spectator)
                {
                    currentTarget.ChangeTeam(CsTeam.Spectator);
                }
                else
                {
                    currentTarget.SwitchTeam((CsTeam)destination);
                }

                return CommandResult.Ok($"Team change requested: {destination}. Server roster rules still apply.");
            }

            if (destination == PlayerTeam.Spectator && target.PawnIsAlive
                && target.PlayerPawn.Value is { IsValid: true } pawn)
            {
                pawn.CommitSuicide(false, true);
                Queue(Move, completion, cancellationToken);
                return null;
            }

            return Move();
        }, completion, cancellationToken);
        return new ValueTask<CommandResult>(completion.Task);
    }

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

    private void Queue(Func<CommandResult?> action, TaskCompletionSource<CommandResult> completion,
        CancellationToken cancellationToken)
    {
        Server.NextWorldUpdate(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = Volatile.Read(ref _disposed) != 0 || !isActive() ? Unavailable() : action();
                if (result is not null)
                {
                    completion.TrySetResult(result);
                }
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "CSS team administration failed.");
                completion.TrySetResult(CommandResult.Fail(CommandFailureReason.HandlerFailed,
                    "Team change failed; check the server log."));
            }
        });
    }

    private CCSPlayerController? Resolve(PlayerSnapshot? expected)
    {
        if (expected is null || !players.TryGet(expected.Id, out var current)
            || current?.IsConnected != true || current.SessionId != expected.SessionId)
        {
            return null;
        }

        return Utilities.GetPlayers().FirstOrDefault(value => IsHuman(value)
            && value.SteamID == expected.Id.SteamId64);
    }

    private static bool IsHuman(CCSPlayerController value)
        => value is { IsValid: true, IsBot: false, IsHLTV: false }
            && value.SteamID > 0 && value.Connected == PlayerConnectedState.Connected;

    private static CommandResult Unavailable() => CommandResult.Fail(CommandFailureReason.HandlerFailed,
        "The player session or team administration is no longer available.");
}
