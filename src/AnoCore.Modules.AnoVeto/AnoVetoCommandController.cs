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
    private readonly AnoVetoCreateHudController _createHud;
    private readonly TimeProvider _timeProvider;
    private IDisposable? _commandRegistration;
    private IDisposable? _presetCommandRegistration;
    private readonly IAnoVetoPresetRepository? _presetRepository;

    public AnoVetoCommandController(
        IAnoCommandRegistry commands,
        ICustomHudService hud,
        IPlayerRegistry players,
        AnoVetoCoordinator coordinator,
        TimeProvider? timeProvider = null,
        IAnoVetoPresetRepository? presetRepository = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _presetRepository = presetRepository;
        _hud = new AnoVetoHudController(
            hud ?? throw new ArgumentNullException(nameof(hud)),
            coordinator,
            _timeProvider);
        _createHud = new AnoVetoCreateHudController(hud, StartRandomFromHudAsync,
            _coordinator.GetAvailableMaps, StartSelectedFromHudAsync, presetRepository);

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
                    new CommandArgumentDescriptor(
                        "query",
                        CommandArgumentKind.String,
                        "Optional search term for find.",
                        required: false),
                ]),
            HandleAsync);

        if (_presetRepository is not null)
            _presetCommandRegistration = commands.Register(
                Owner,
                new CommandDescriptor(
                    "anovetopreset",
                    "List, rename, or delete your own saved Veto pools.",
                    arguments:
                    [
                        new CommandArgumentDescriptor("action", CommandArgumentKind.String, "list, rename or delete"),
                        new CommandArgumentDescriptor("name", CommandArgumentKind.String, "Existing preset name", required: false),
                        new CommandArgumentDescriptor("newname", CommandArgumentKind.String, "New preset name", required: false),
                    ]),
                HandlePresetCommandAsync);
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
        Interlocked.Exchange(ref _presetCommandRegistration, null)?.Dispose();
        _createHud.Dispose();
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
            "create" => ValueTask.FromResult(OpenCreateHud(context.Caller)),
            "random" => CreateAsync(context),
            "status" => ValueTask.FromResult(GetStatus()),
            "find" => ValueTask.FromResult(FindInPicker(context)),
            "cancel" => CancelAsync(context),
            _ => ValueTask.FromResult(CommandResult.Fail(
                CommandFailureReason.InvalidInput,
                $"Unknown AnoVeto action '{action}'.")),
        };
    }

    private async ValueTask<CommandResult> HandlePresetCommandAsync(CommandContext context)
    {
        if (context.Caller is null || _presetRepository is null)
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "Personal Veto presets require a player and database.");

        if (!context.TryGet<string>("action", out var action))
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "Use list, rename or delete.");

        var owner = context.Caller.SteamId64;
        try
        {
            switch (action.Trim().ToLowerInvariant())
            {
                case "list":
                    var presets = await _presetRepository.ListAsync(owner, context.CancellationToken).ConfigureAwait(false);
                    return CommandResult.Ok(presets.Count == 0
                        ? "You have no saved Veto presets."
                        : "Your saved vetos: " + string.Join(", ", presets.Select(p => p.Preset.Name)));
                case "rename":
                    if (!context.TryGet<string>("name", out var oldName)
                        || !context.TryGet<string>("newname", out var newName))
                        return CommandResult.Fail(CommandFailureReason.InvalidInput,
                            "Usage: !anovetopreset rename <oldname> <newname>. Quote names containing spaces.");
                    var stored = await _presetRepository.GetAsync(owner, oldName).ConfigureAwait(false);
                    if (stored is null)
                        return CommandResult.Fail(CommandFailureReason.InvalidInput, "Saved Veto not found.");
                    await _presetRepository.ReplaceAsync(
                        owner, oldName, stored.Value.Revision, stored.Value.Preset.Rename(newName),
                        context.CancellationToken).ConfigureAwait(false);
                    return CommandResult.Ok($"Renamed Veto to {newName}.");
                case "delete":
                    if (!context.TryGet<string>("name", out var toDelete))
                        return CommandResult.Fail(CommandFailureReason.InvalidInput,
                            "Usage: !anovetopreset delete <name>. Quote names containing spaces.");
                    return CommandResult.Fail(CommandFailureReason.InvalidInput,
                        "Delete through Use Veto with two-click confirmation.");
                default:
                    return CommandResult.Fail(CommandFailureReason.InvalidInput, "Use list or rename.");
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return CommandResult.Fail(CommandFailureReason.HandlerFailed, "Preset operation failed.");
        }
    }

    private CommandResult FindInPicker(CommandContext context)
    {
        var query = context.TryGet<string>("query", out var value) ? value : string.Empty;
        if (query.Length > 80)
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "Search term is too long.");
        return _createHud.ApplyChatFilter(context.Caller!, query)
            ? CommandResult.Ok("Veto map list filtered.")
            : CommandResult.Fail(CommandFailureReason.InvalidInput,
                "Open Create Veto and select the manual map list first.");
    }

    private CommandResult OpenCreateHud(PlayerId player)
    {
        if (_coordinator.TryGetStatus(out _))
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "An AnoVeto vote is already active.");
        return _createHud.Show(player)
            ? CommandResult.Ok("AnoVeto creation menu opened.")
            : CommandResult.Fail(CommandFailureReason.HandlerFailed, "AnoVeto creation menu could not be opened.");
    }

    private async ValueTask<bool> StartSelectedFromHudAsync(
        PlayerId manager, IReadOnlyList<string> selectedMaps, CancellationToken cancellationToken)
    {
        var eligible = _players.OnlinePlayers
            .Where(player => player.IsConnected)
            .Select(player => player.Id)
            .Distinct()
            .ToArray();
        if (!eligible.Contains(manager)) return false;
        var result = await _coordinator.CreateSelectedAsync(
            manager, eligible, selectedMaps, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (!result.Accepted) return false;
        _hud.ShowAll(eligible);
        return true;
    }

    private async ValueTask<bool> StartRandomFromHudAsync(PlayerId manager, CancellationToken cancellationToken)
    {
        // Permission is checked by the authoritative VoteService inside the coordinator.
        // UI clicks never bypass the normal vote creation policy.
        var eligible = _players.OnlinePlayers
            .Where(player => player.IsConnected)
            .Select(player => player.Id)
            .Distinct()
            .ToArray();
        if (eligible.Length == 0 || !eligible.Contains(manager))
            return false;
        var result = await _coordinator.CreateAsync(
            manager, eligible, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (!result.Accepted) return false;
        _hud.ShowAll(eligible);
        return true;
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
