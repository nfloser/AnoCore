using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Maps;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.AnoVeto;

public sealed class AnoVetoCommandController : IDisposable
{
    private static readonly ModuleId Owner = new("ano.veto");
    private static readonly MenuId VoteMenuId = new("ano.anoveto");

    private readonly object _menuGate = new();
    private readonly IMenuService _menus;
    private readonly IPlayerRegistry _players;
    private readonly AnoVetoCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;
    private IDisposable? _commandRegistration;
    private IDisposable? _menuRegistration;

    public AnoVetoCommandController(
        IAnoCommandRegistry commands,
        IMenuService menus,
        IPlayerRegistry players,
        AnoVetoCoordinator coordinator,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _menus = menus ?? throw new ArgumentNullException(nameof(menus));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _timeProvider = timeProvider ?? TimeProvider.System;

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
            RemoveVoteMenu();
        }

        return result;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _commandRegistration, null)?.Dispose();
        RemoveVoteMenu();
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
            return ValueTask.FromResult(OpenMenu(context.Caller));
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
            ReplaceVoteMenu(result.Maps);
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
            RemoveVoteMenu();
            return CommandResult.Ok("AnoVeto cancelled.");
        }

        return result.Failure switch
        {
            AnoVetoFailure.Forbidden => CommandResult.Fail(CommandFailureReason.Forbidden, "You are not allowed to cancel AnoVeto votes."),
            AnoVetoFailure.NotActive => CommandResult.Fail(CommandFailureReason.InvalidInput, "There is no active AnoVeto vote."),
            _ => CommandResult.Fail(CommandFailureReason.HandlerFailed, $"AnoVeto could not be cancelled: {result.Failure}."),
        };
    }

    private CommandResult OpenMenu(PlayerId playerId)
    {
        if (!_coordinator.TryGetStatus(out var maps))
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "There is no active AnoVeto vote.");
        }

        EnsureVoteMenu(maps);
        _menus.Open(playerId, VoteMenuId);
        return CommandResult.Ok("AnoVeto menu opened.");
    }

    private void EnsureVoteMenu(IReadOnlyList<MapDefinition> maps)
    {
        lock (_menuGate)
        {
            if (_menuRegistration is null)
            {
                _menuRegistration = RegisterVoteMenu(maps);
            }
        }
    }

    private void ReplaceVoteMenu(IReadOnlyList<MapDefinition> maps)
    {
        lock (_menuGate)
        {
            _menuRegistration?.Dispose();
            _menuRegistration = RegisterVoteMenu(maps);
        }
    }

    private void RemoveVoteMenu()
    {
        lock (_menuGate)
        {
            _menuRegistration?.Dispose();
            _menuRegistration = null;
        }
    }

    private IDisposable RegisterVoteMenu(IReadOnlyList<MapDefinition> maps)
        => _menus.Register(
            Owner,
            new MenuDefinition(
                VoteMenuId,
                "AnoVeto — choose the next map",
                maps.Select((map, index) => new MenuOption(
                    $"map{index + 1:00}",
                    map.DisplayName,
                    context => CastFromMenuAsync(context, map))).ToArray()));

    private async ValueTask CastFromMenuAsync(MenuSelectionContext context, MapDefinition map)
    {
        var result = await _coordinator.CastAsync(
            context.PlayerId,
            map.MapId,
            _timeProvider.GetUtcNow(),
            context.CancellationToken).ConfigureAwait(false);
        if (result.Accepted && result.Outcome != AnoVetoOutcome.None)
        {
            RemoveVoteMenu();
        }
    }
}
