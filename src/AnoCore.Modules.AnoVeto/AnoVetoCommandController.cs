using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.AnoVeto;

public sealed class AnoVetoCommandController : IDisposable
{
    private static readonly ModuleId Owner = new("ano.veto");

    private readonly IPlayerRegistry _players;
    private readonly AnoVetoCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;
    private IDisposable? _registration;

    public AnoVetoCommandController(
        IAnoCommandRegistry commands,
        IPlayerRegistry players,
        AnoVetoCoordinator coordinator,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _timeProvider = timeProvider ?? TimeProvider.System;

        _registration = commands.Register(
            Owner,
            new CommandDescriptor(
                "anoveto",
                "Open or manage the AnoVeto map vote.",
                arguments:
                [
                    new CommandArgumentDescriptor(
                        "action",
                        CommandArgumentKind.String,
                        "Optional management action such as create.",
                        required: false),
                ]),
            HandleAsync);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _registration, null)?.Dispose();
    }

    private async ValueTask<CommandResult> HandleAsync(CommandContext context)
    {
        if (context.Caller is null)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "AnoVeto must be used by a player.");
        }

        if (!context.TryGet<string>("action", out var action) || string.IsNullOrWhiteSpace(action))
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "Use !anoveto create to start a vote.");
        }

        if (!string.Equals(action, "create", StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, $"Unknown AnoVeto action '{action}'.");
        }

        var eligiblePlayers = _players.OnlinePlayers
            .Where(player => player.IsConnected)
            .Select(player => player.Id)
            .Distinct()
            .ToArray();

        if (eligiblePlayers.Length == 0)
        {
            return CommandResult.Fail(CommandFailureReason.HandlerFailed, "No eligible players are online.");
        }

        var result = await _coordinator.CreateAsync(
            context.Caller,
            eligiblePlayers,
            _timeProvider.GetUtcNow(),
            context.CancellationToken).ConfigureAwait(false);

        if (result.Accepted)
        {
            return CommandResult.Ok($"AnoVeto started with {result.Maps.Count} maps.");
        }

        return result.Failure switch
        {
            AnoVetoFailure.Forbidden => CommandResult.Fail(CommandFailureReason.Forbidden, "You are not allowed to create AnoVeto votes."),
            AnoVetoFailure.AlreadyActive => CommandResult.Fail(CommandFailureReason.InvalidInput, "An AnoVeto vote is already active."),
            AnoVetoFailure.NotEnoughMaps => CommandResult.Fail(CommandFailureReason.HandlerFailed, "At least eight configured maps are required."),
            _ => CommandResult.Fail(CommandFailureReason.HandlerFailed, $"AnoVeto could not be started: {result.Failure}."),
        };
    }
}
