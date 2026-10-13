using System.Net;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Modules.Tournament;

/// <summary>Session-pinned operator menus invoke the same authorized/audited commands as console users.</summary>
public sealed class TournamentAdminMenuModule : IDisposable
{
    private static readonly ModuleId Owner = new("ano.tournament-admin-menu");
    private readonly IAnoCommandRegistry _commands;
    private readonly IPlayerRegistry _players;
    private readonly IMenuService _menus;
    private readonly IConfigStore _config;
    private readonly ITournamentEventRepository _events;
    private readonly object _gate = new();
    private readonly Dictionary<PlayerId, (PlayerSnapshot Player, IDisposable Registration, MenuDefinition Menu)> _owned = [];
    private readonly List<IDisposable> _registrations = [];
    private bool _disposed;
    public TournamentAdminMenuModule(IAnoCommandRegistry commands, IPlayerRegistry players, IMenuService menus, IConfigStore config,
        ITournamentEventRepository events, IAnoEventBus eventBus)
    {
        _commands = commands; _players = players; _menus = menus; _config = config; _events = events;
        _registrations.Add(commands.Register(Owner, new("anotournamentadmin", "Open authorized tournament administration.", new("ano.tournament.manage")), context =>
        {
            if (context.Caller is null || !players.TryGet(context.Caller, out var actor) || actor is not { IsConnected: true })
                return ValueTask.FromResult(CommandResult.Fail(CommandFailureReason.Forbidden, "A connected player is required."));
            Root(actor); return ValueTask.FromResult(CommandResult.Ok("[ANO] Tournament administration opened."));
        }));
        _registrations.Add(eventBus.Subscribe<PlayerDisconnectedEvent>((value, _) => { Remove(value.Player); return ValueTask.CompletedTask; }));
        _registrations.Add(eventBus.Subscribe<PlayerReconnectedEvent>((value, _) => { Remove(value.Previous); return ValueTask.CompletedTask; }));
    }
    private void Root(PlayerSnapshot actor)
    {
        var options = new List<MenuOption>
        {
            new("status","Shared event status",context=>Run(actor,"!anotournamenteventstatus",context.CancellationToken),true),
            new("preview","Preview fixed configuration",context=>Run(actor,"!anotournamenteventpreview",context.CancellationToken),true),
            new("publish","Preview and confirm publication",context=>Confirm(actor,context.CancellationToken),true),
        };
        if (_commands.GetCommands().Any(command => command.Name == "anotournamentassignments"))
            options.Add(new("assignments", "Server assignments", context => Run(actor, "!anotournamentassignments", context.CancellationToken), true));
        foreach (var preset in Enum.GetValues<TournamentEventPreset>())
            options.Add(new("preset-" + preset.ToString().ToLowerInvariant(), "Stage preset: " + preset, context =>
                Run(actor, "!anotournamenteventpreset " + preset, context.CancellationToken), true));
        Show(actor, "Tournament administration", options);
    }
    private async ValueTask Confirm(PlayerSnapshot actor, CancellationToken token)
    {
        var expected = CurrentMenu(actor); if (expected is null) return;
        try
        {
            var document = await _config.LoadAsync(TournamentEventCommandController.DefinitionConfigName, () => new TournamentEventImportConfiguration(),
                TournamentEventImportConfiguration.Validate, token).ConfigureAwait(false);
            var preview = TournamentEventPresets.Preview(document.Event);
            var stored = await _events.LoadAsync(document.Event.Id, token).ConfigureAwait(false);
            var command = "!anotournamenteventload " + (stored?.Revision ?? 0) + " " + TournamentEventImportConfiguration.Fingerprint(document);
            lock (_gate)
            {
                if (CurrentMenu(actor) != expected) return;
                Show(actor, "Confirm tournament publication",
                    [Info("summary",$"{document.Event.Name}: {preview.Teams} teams, {preview.MinimumMatches}-{preview.MaximumMatches} matches"),
                 new("confirm","Publish this preview",context=>Run(actor,command,context.CancellationToken),true),Back(actor)]);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { if (CurrentMenu(actor) == expected) Show(actor, "Configuration rejected", [Info("error", exception.Message), Back(actor)]); }
    }
    private async ValueTask Run(PlayerSnapshot actor, string command, CancellationToken token)
    {
        MenuDefinition? expected;
        lock (_gate)
        {
            expected = CurrentMenu(actor); if (expected is null) return;
            _menus.Close(actor.Id); // Consume before awaiting: a second click cannot replay a confirmation.
        }
        var result = await _commands.ExecuteAsync(command, actor.Id, token).ConfigureAwait(false);
        lock (_gate)
        {
            if (!Current(actor) || !_owned.TryGetValue(actor.Id, out var state) || state.Menu != expected
                || _menus.TryGetOpenMenu(actor.Id, out _)) return;
            Show(actor, "Tournament action result", [Info("result", result.Message ?? "Done."), Back(actor)]);
        }
    }
    private MenuOption Back(PlayerSnapshot actor) => new("back", "Back to tournament actions", _ => { if (Current(actor)) Root(actor); return ValueTask.CompletedTask; }, true);
    private static MenuOption Info(string id, string text) => new(id, Text(text), _ => ValueTask.CompletedTask, true);
    private static string Text(string value) => WebUtility.HtmlEncode(new string(value.Where(c => !char.IsControl(c)).Take(200).ToArray()));
    private bool Current(PlayerSnapshot actor) => !_disposed && _players.TryGet(actor.Id, out var current)
        && current is { IsConnected: true } && current.SessionId == actor.SessionId;
    private MenuDefinition? CurrentMenu(PlayerSnapshot actor)
    {
        lock (_gate) return Current(actor) && _owned.TryGetValue(actor.Id, out var state) && state.Player.SessionId == actor.SessionId
            && _menus.TryGetOpenMenu(actor.Id, out var open) && open == state.Menu ? open : null;
    }
    private void Show(PlayerSnapshot actor, string title, IReadOnlyCollection<MenuOption> options)
    {
        lock (_gate)
        {
            if (!Current(actor)) return;
            if (_owned.Remove(actor.Id, out var previous)) previous.Registration.Dispose();
            var menu = new MenuDefinition(new("ano.tournament.admin." + actor.Id.SteamId64), title, options);
            _owned[actor.Id] = (actor, _menus.Register(Owner, menu), menu); _menus.Open(actor.Id, menu.Id);
        }
    }
    private void Remove(PlayerSnapshot actor)
    { lock (_gate) if (_owned.TryGetValue(actor.Id, out var state) && state.Player.SessionId == actor.SessionId) { state.Registration.Dispose(); _owned.Remove(actor.Id); } }
    public void Dispose()
    { lock (_gate) { if (_disposed) return; _disposed = true; foreach (var registration in _registrations) registration.Dispose(); foreach (var state in _owned.Values) state.Registration.Dispose(); _owned.Clear(); } }
}
