using System.Net;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Runtime.Menus;

/// <summary>Navigation over registered feature menus and existing read commands.</summary>
public sealed class AnoHomeMenuModule : IDisposable
{
    public const string CommandName = "anomenu";
    private static readonly ModuleId Owner = new("core.home-menu");
    private static readonly (string Name, string Label, bool Menu, bool Paged)[] Destinations =
    [
        ("anostatsmenu", "Statistics", true, false),
        ("anoranks", "Ranks", true, false),
        ("anoprogression", "Progression", true, false),
        ("anochallenges", "Challenges", false, true),
        ("anoachievements", "Achievements", false, true),
        ("anorating", "AnoRating", false, false),
        ("anosettingsmenu", "Settings", true, false),
        ("anochatmenu", "Chat tags", true, false),
        ("anotournamentstatus", "Tournament status", false, false),
    ];
    private readonly object _gate = new();
    private readonly IAnoCommandRegistry _commands;
    private readonly IPlayerRegistry _players;
    private readonly IMenuService _menus;
    private readonly IPermissionEvaluator _permissions;
    private readonly Dictionary<PlayerId, (PlayerSessionId Session, long Request, IDisposable? Registration, MenuDefinition? Expected)> _owned = [];
    private readonly List<IDisposable> _registrations = [];
    private readonly CancellationTokenSource _lifetime = new();
    private long _request;
    private bool _disposed;

    public AnoHomeMenuModule(IAnoCommandRegistry commands, IPlayerRegistry players,
        IMenuService menus, IPermissionEvaluator permissions, IAnoEventBus events)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _menus = menus ?? throw new ArgumentNullException(nameof(menus));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        ArgumentNullException.ThrowIfNull(events);
        try
        {
            _registrations.Add(commands.Register(Owner, new(CommandName, "Open the AnoCore home menu."), OpenAsync));
            _registrations.Add(events.Subscribe<PlayerDisconnectedEvent>((value, _) =>
            {
                RemoveSession(value.Player);
                return ValueTask.CompletedTask;
            }));
            _registrations.Add(events.Subscribe<PlayerReconnectedEvent>((value, _) =>
            {
                RemoveSession(value.Previous);
                return ValueTask.CompletedTask;
            }));
        }
        catch
        {
            foreach (var registration in _registrations) registration.Dispose();
            throw;
        }
    }

    private async ValueTask<CommandResult> OpenAsync(CommandContext context)
    {
        if (context.Caller is null || !_players.TryGet(context.Caller, out var player)
            || player is not { IsConnected: true } || !Current(player))
            return CommandResult.Fail(CommandFailureReason.Forbidden, "A connected player is required.");
        await RootAsync(player, context.CancellationToken).ConfigureAwait(false);
        return CommandResult.Ok("[ANO] Home menu opened.");
    }

    private async ValueTask RootAsync(PlayerSnapshot player, CancellationToken cancellationToken)
    {
        var request = Begin(player);
        var descriptors = _commands.GetCommands().ToDictionary(command => command.Name, StringComparer.Ordinal);
        var options = new List<MenuOption>();
        foreach (var view in Destinations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!descriptors.TryGetValue(view.Name, out var descriptor)
                || !await Allowed(player, descriptor, cancellationToken).ConfigureAwait(false)) continue;
            options.Add(new(view.Name, view.Label, context => Current(player) && context.PlayerId == player.Id
                ? ViewAsync(player, view, 1, context.CancellationToken) : ValueTask.CompletedTask, keepOpen: true));
        }
        var administration = descriptors.Values.Where(command => command.Permission is not null).ToArray();
        foreach (var command in administration)
        {
            if (await Allowed(player, command, cancellationToken).ConfigureAwait(false))
            {
                options.Add(new("administration", "Administration command reference", context =>
                    Current(player) && context.PlayerId == player.Id
                        ? AdministrationAsync(player, context.CancellationToken) : ValueTask.CompletedTask, keepOpen: true));
                break;
            }
        }
        if (options.Count == 0) options.Add(Info("empty", "No enabled menu destinations."));
        Replace(player, "AnoCore", options, request);
    }

    private async ValueTask ViewAsync(PlayerSnapshot player,
        (string Name, string Label, bool Menu, bool Paged) view, int page, CancellationToken cancellationToken)
    {
        var request = Begin(player);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        // The registry rechecks permissions at execution, including after revocation.
        var result = await _commands.ExecuteAsync("!" + view.Name + (view.Paged ? " " + page : ""),
            player.Id, linked.Token).ConfigureAwait(false);
        if (view.Menu && result.Success) return;
        var options = (result.Message ?? "No data available.").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Take(24).Select((line, index) => Info("line" + index, Text(line))).ToList();
        if (view.Paged && page > 1) options.Add(new("previous", "Previous page", context =>
            Current(player) ? ViewAsync(player, view, page - 1, context.CancellationToken) : ValueTask.CompletedTask, keepOpen: true));
        if (view.Paged && result.Success && page < 1000) options.Add(new("next", "Next page", context =>
            Current(player) ? ViewAsync(player, view, page + 1, context.CancellationToken) : ValueTask.CompletedTask, keepOpen: true));
        options.Add(Back(player));
        Replace(player, view.Label + (view.Paged ? " — page " + page : ""), options, request);
    }

    private async ValueTask AdministrationAsync(PlayerSnapshot player, CancellationToken cancellationToken)
    {
        var request = Begin(player);
        var options = new List<MenuOption>();
        foreach (var command in _commands.GetCommands().Where(command => command.Permission is not null).OrderBy(command => command.Name))
        {
            if (!await Allowed(player, command, cancellationToken).ConfigureAwait(false)) continue;
            // A reference row never executes a mutation without its required target/arguments.
            options.Add(Info("command" + options.Count, Text("!" + command.Usage)));
        }
        if (options.Count == 0) options.Add(Info("empty", "No authorized administration commands."));
        options.Add(Back(player));
        Replace(player, "Administration — command reference", options, request);
    }

    private ValueTask<bool> Allowed(PlayerSnapshot player, CommandDescriptor command, CancellationToken cancellationToken)
        => command.Permission is null ? ValueTask.FromResult(true)
            : _permissions.HasPermissionAsync(player.Id, command.Permission, cancellationToken);
    private MenuOption Back(PlayerSnapshot player) => new("home", "Back to Home", context =>
        Current(player) ? RootAsync(player, context.CancellationToken) : ValueTask.CompletedTask, keepOpen: true);
    private static MenuOption Info(string id, string label) => new(id, label, _ => ValueTask.CompletedTask, keepOpen: true);
    private static string Text(string text)
    {
        var safe = new string(WebUtility.HtmlDecode(text).Where(character => !char.IsControl(character)).Take(240).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "Unavailable" : WebUtility.HtmlEncode(safe);
    }
    private bool Current(PlayerSnapshot player)
        => !_disposed && _players.TryGet(player.Id, out var current) && current is { IsConnected: true }
            && current.SessionId == player.SessionId;
    private long Begin(PlayerSnapshot player)
    {
        lock (_gate)
        {
            var request = checked(++_request);
            var registration = _owned.GetValueOrDefault(player.Id).Registration;
            _menus.TryGetOpenMenu(player.Id, out var expected);
            _owned[player.Id] = (player.SessionId, request, registration, expected);
            return request;
        }
    }
    private void Replace(PlayerSnapshot player, string title, IReadOnlyCollection<MenuOption> options, long request)
    {
        lock (_gate)
        {
            if (!Current(player) || !_owned.TryGetValue(player.Id, out var state) || state.Request != request) return;
            _menus.TryGetOpenMenu(player.Id, out var currentMenu);
            if (!ReferenceEquals(currentMenu, state.Expected)) return;
            state.Registration?.Dispose();
            var menu = new MenuDefinition(new("ano.home." + player.Id.SteamId64), title, options);
            _owned[player.Id] = (player.SessionId, request, _menus.Register(Owner, menu), menu);
            _menus.Open(player.Id, menu.Id);
        }
    }
    private void RemoveSession(PlayerSnapshot player)
    {
        lock (_gate)
            if (_owned.TryGetValue(player.Id, out var state) && state.Session == player.SessionId)
            {
                state.Registration?.Dispose();
                _owned.Remove(player.Id);
            }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            foreach (var registration in _registrations) registration.Dispose();
            foreach (var state in _owned.Values) state.Registration?.Dispose();
            _owned.Clear();
        }
    }
}
