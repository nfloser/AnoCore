using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.AnoVeto;

public sealed class AnoVetoCommandController : IDisposable
{
    private static readonly ModuleId Owner = new("ano.veto");

    private readonly IPlayerRegistry _players;
    private readonly AnoVetoCoordinator _coordinator;
    private readonly AnoVetoHudController _hud;
    private readonly TimeProvider _timeProvider;
    private IDisposable? _commandRegistration;

    public AnoVetoCommandController(
        IAnoCommandRegistry commands,
        ICustomHudService hud,
        IPlayerRegistry players,
        AnoVetoCoordinator coordinator,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _hud = new AnoVetoHudController(
            hud ?? throw new ArgumentNullException(nameof(hud)),
            coordinator,
            _timeProvider);

        _commandRegistration = commands.Register(
            Owner,
            new CommandDescriptor(
                "anoveto",
                "Open or manage the AnoVeto map vote.",
                arguments:
                [
                    new CommandArgumentDescriptor(
                        "action",
                        CommandArgumentKind.String,
                        "Optional management action such as create, status or cancel.",
                        required: false),
                ]),
            HandleAsync);
    }

    public async ValueTask<AnoVetoOperationResult?> ExpireAsync(CancellationToken cancellationToken = default)
    {
        var result = await _coordinator.ExpireAsync(
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        if (result is not null)
        {
            _hud.HideAll();
        }

        return result;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _commandRegistration, null)?.Dispose();
        _hud.Dispose();
    }

    private ValueTask<CommandResult> HandleAsync(CommandContext context)
    {
        if (context.Caller is null)
        {
            return ValueTask.FromResult(CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "AnoVeto must be used by a player."));
        }

        if (!context.TryGet<string>("action", out var action) || string.IsNullOrWhiteSpace(action))
        {
            return ValueTask.FromResult(OpenHud(context.Caller));
        }

        return action.Trim().ToLowerInvariant() switch
        {
            "create" => CreateAsync(context),
            "status" => ValueTask.FromResult(GetStatus()),
            "cancel" => CancelAsync(context),
            _ => ValueTask.FromResult(CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                $"Unknown AnoVeto action '{action}'.")),
        };
    }

    private async ValueTask<CommandResult> CreateAsync(CommandContext context)
    {
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
            context.Caller!,
            eligiblePlayers,
            _timeProvider.GetUtcNow(),
            context.CancellationToken).ConfigureAwait(false);

        if (result.Accepted)
        {
            _hud.ShowAll(eligiblePlayers);
            return CommandResult.Ok($"AnoVeto started with {result.Maps.Count} maps.");
        }

        return result.Failure switch
        {
            AnoVetoFailure.Forbidden => CommandResult.Fail(CommandFailureReason.Forbidden, "You are not allowed to create AnoVeto votes."),
            AnoVetoFailure.AlreadyActive => CommandResult.Fail(CommandFailureReason.InvalidInput, "An AnoVeto vote is already active."),
            AnoVetoFailure.NotEnoughMaps => CommandResult.Fail(CommandFailureReason.HandlerFailed, "At least eight configured maps are required."),
            AnoVetoFailure.NotEnoughEligiblePlayers => CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                "Not enough eligible players are online for the configured minimum vote count."),
            _ => CommandResult.Fail(CommandFailureReason.HandlerFailed, $"AnoVeto could not be started: {result.Failure}."),
        };
    }

    private CommandResult GetStatus()
    {
        if (!_coordinator.TryGetStatus(out var maps))
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "There is no active AnoVeto vote.");
        }

        return CommandResult.Ok($"AnoVeto is active with {maps.Count} maps.");
    }

    private async ValueTask<CommandResult> CancelAsync(CommandContext context)
    {
        var result = await _coordinator.CancelAsync(
            context.Caller!,
            _timeProvider.GetUtcNow(),
            context.CancellationToken).ConfigureAwait(false);

        if (result.Accepted)
        {
            _hud.HideAll();
            return CommandResult.Ok("AnoVeto cancelled.");
        }

        return result.Failure switch
        {
            AnoVetoFailure.Forbidden => CommandResult.Fail(CommandFailureReason.Forbidden, "You are not allowed to cancel AnoVeto votes."),
            AnoVetoFailure.NotActive => CommandResult.Fail(CommandFailureReason.InvalidInput, "There is no active AnoVeto vote."),
            _ => CommandResult.Fail(CommandFailureReason.HandlerFailed, $"AnoVeto could not be cancelled: {result.Failure}."),
        };
    }

    private CommandResult OpenHud(PlayerId playerId)
    {
        if (!_coordinator.TryGetStatus(out _))
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "There is no active AnoVeto vote.");
        }

        return _hud.Show(playerId)
            ? CommandResult.Ok("AnoVeto HUD opened.")
            : CommandResult.Fail(CommandFailureReason.HandlerFailed, "AnoVeto HUD could not be opened.");
    }
}
