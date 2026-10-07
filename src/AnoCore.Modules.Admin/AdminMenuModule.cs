using System.Net;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Abstractions.Targeting;

namespace AnoCore.Modules.Admin;

/// <summary>Session-pinned confirmation forms over the existing audited command path.</summary>
public sealed class AdminMenuModule : IDisposable
{
    public const string CommandName = "anoadminmenu";
    private static readonly ModuleId Owner = new("ano.admin.menu");
    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        "anokick", "anosilentkick", "anoban", "anounban", "anomute", "anounmute", "anogag", "anoungag", "anosilence", "anounsilence",
        "anohealth", "anoarmor", "anofreeze", "anounfreeze", "anonoclip", "anoslay", "anospeed", "anoblind", "anounblind", "anogod", "anoungod",
        "anowalk", "anoresetspeed", "anorespawn", "anorevive", "anobury", "anounbury", "anostrip", "anoteam", "anoswap",
    };
    private static readonly Dictionary<string, string[]> Choices = new(StringComparer.Ordinal)
    {
        ["minutes"] = ["5", "15", "60", "1440"],
        ["reason"] = ["Rule violation", "Abusive communication", "Administrator decision"],
        ["health"] = ["1", "50", "100"],
        ["armor"] = ["0", "50", "100"],
        ["percent"] = ["50", "100", "150", "200"],
        ["alpha"] = ["0", "128", "255"],
        ["team"] = ["t", "ct", "spec"],
    };
    private readonly object _gate = new();
    private readonly IAnoCommandRegistry _commands;
    private readonly IPlayerRegistry _players;
    private readonly IMenuService _menus;
    private readonly IPermissionEvaluator _permissions;
    private readonly ITargetAuthorizationService _targets;
    private readonly Dictionary<PlayerId, State> _owned = [];
    private readonly List<IDisposable> _registrations = [];
    private long _revision;
    private bool _disposed;

    public AdminMenuModule(IAnoCommandRegistry commands, IPlayerRegistry players, IMenuService menus,
        IPermissionEvaluator permissions, ITargetAuthorizationService targets, IAnoEventBus events)
    {
        _commands = commands;
        _players = players;
        _menus = menus;
        _permissions = permissions;
        _targets = targets;
        try
        {
            _registrations.Add(commands.Register(Owner, new(CommandName, "Open authorized administration actions.", new("ano.admin.menu")), async context =>
            {
                if (context.Caller is null || !_players.TryGet(context.Caller, out var actor) || actor is not { IsConnected: true })
                    return CommandResult.Fail(CommandFailureReason.Forbidden, "A connected player is required.");
                await Root(actor, context.CancellationToken);
                return CommandResult.Ok("[ANO] Administration menu opened.");
            }));
            _registrations.Add(events.Subscribe<PlayerDisconnectedEvent>((value, _) => { Remove(value.Player); return ValueTask.CompletedTask; }));
            _registrations.Add(events.Subscribe<PlayerReconnectedEvent>((value, _) => { Remove(value.Previous); return ValueTask.CompletedTask; }));
        }
        catch { Dispose(); throw; }
    }

    private async ValueTask Root(PlayerSnapshot actor, CancellationToken token)
    {
        var revision = Begin(actor);
        var options = new List<MenuOption>();
        foreach (var command in _commands.GetCommands().Where(command => Supported.Contains(command.Name)).OrderBy(command => command.Name))
        {
            if (command.Permission is null || command.Arguments.FirstOrDefault()?.Name != "target"
                || command.Arguments.Skip(1).Any(argument => !Choices.ContainsKey(argument.Name))
                || !await _permissions.HasPermissionAsync(actor.Id, command.Permission, token)) continue;
            options.Add(new(command.Name, Text(command.Description), context => Current(actor)
                ? Targets(actor, command, context.CancellationToken) : ValueTask.CompletedTask, keepOpen: true));
        }
        if (options.Count == 0) options.Add(Info("empty", "No authorized actions available."));
        Replace(actor, "Administration — action", options, revision);
    }

    private async ValueTask Targets(PlayerSnapshot actor, CommandDescriptor command, CancellationToken token)
    {
        var revision = Begin(actor);
        var options = new List<MenuOption>();
        foreach (var target in _players.OnlinePlayers.OrderBy(value => value.Name, StringComparer.Ordinal).ThenBy(value => value.Id.SteamId64).Take(128))
        {
            if (!await Allowed(actor, target, command, token)) continue;
            options.Add(new(target.Id.SteamId64.ToString(), Text(target.Name) + " · " + target.Id.SteamId64,
                context => Current(actor) && Current(target)
                    ? Argument(actor, target, command, [], 1, context.CancellationToken) : ValueTask.CompletedTask, keepOpen: true));
        }
        if (options.Count == 0) options.Add(Info("empty", "No eligible connected targets."));
        options.Add(Back(actor));
        Replace(actor, "Administration — target", options, revision);
    }

    private async ValueTask Argument(PlayerSnapshot actor, PlayerSnapshot target, CommandDescriptor command,
        IReadOnlyList<string> values, int index, CancellationToken token)
    {
        var revision = Begin(actor);
        if (!await Allowed(actor, target, command, token))
        {
            Replace(actor, "Administration — denied", [Info("denied", "Target/session/permission changed."), Back(actor)], revision);
            return;
        }
        if (index < command.Arguments.Count)
        {
            var argument = command.Arguments[index];
            var choices = command.Name == "anoarmor" && argument.Name == "value" ? new[] { "0", "50", "100" } : Choices[argument.Name];
            var options = choices.Select(value => new MenuOption(value, Text(value), context =>
                Current(actor) && Current(target) ? Argument(actor, target, command, values.Append(value).ToArray(), index + 1,
                    context.CancellationToken) : ValueTask.CompletedTask, keepOpen: true)).Append(Back(actor)).ToArray();
            Replace(actor, "Administration — " + argument.Name, options, revision);
            return;
        }
        Replace(actor, "Confirm " + Text(command.Name) + " → " + Text(target.Name),
            [Info("summary", string.Join(" · ", values)),
                new("confirm", "Confirm action", context => Execute(actor, target, command, values, context.CancellationToken), keepOpen: true),
                Back(actor)], revision);
    }

    private async ValueTask Execute(PlayerSnapshot actor, PlayerSnapshot target, CommandDescriptor command,
        IReadOnlyList<string> values, CancellationToken token)
    {
        // Consume the exact confirmation before awaiting; a second click cannot replay it.
        lock (_gate)
        {
            if (!Current(actor) || !_owned.TryGetValue(actor.Id, out var state)
                || !_menus.TryGetOpenMenu(actor.Id, out var menu) || !ReferenceEquals(menu, state.Expected)) return;
            _menus.Close(actor.Id);
        }
        var revision = Begin(actor);
        var result = await Allowed(actor, target, command, token)
            ? await _commands.ExecuteAsync("!" + command.Name + " " + target.Id.SteamId64 +
                string.Concat(values.Select(value => " \"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"")), actor.Id, token)
            : CommandResult.Fail(CommandFailureReason.Forbidden, "Target/session/permission changed.");
        Replace(actor, "Administration — result", [Info("result", Text(result.Message ?? "Action completed.")), Back(actor)], revision);
    }

    private async ValueTask<bool> Allowed(PlayerSnapshot actor, PlayerSnapshot target, CommandDescriptor command, CancellationToken token)
    {
        if (!Current(actor) || !Current(target) || command.Permission is null) return false;
        var decision = await _targets.AuthorizeAsync(actor.Id, target, command.Permission, cancellationToken: token);
        return decision.IsAllowed && Current(actor) && Current(target);
    }
    private bool Current(PlayerSnapshot player) => !_disposed && _players.TryGet(player.Id, out var current)
        && current is { IsConnected: true } && current.SessionId == player.SessionId;
    private MenuOption Back(PlayerSnapshot actor) => new("back", "Back to actions", context => Current(actor)
        ? Root(actor, context.CancellationToken) : ValueTask.CompletedTask, keepOpen: true);
    private static MenuOption Info(string id, string label) => new(id, label, _ => ValueTask.CompletedTask, keepOpen: true);
    private static string Text(string text) => WebUtility.HtmlEncode(new string(WebUtility.HtmlDecode(text).Where(value => !char.IsControl(value)).Take(180).ToArray()));
    private long Begin(PlayerSnapshot actor)
    {
        lock (_gate)
        {
            _menus.TryGetOpenMenu(actor.Id, out var menu);
            var revision = ++_revision;
            _owned[actor.Id] = new(actor, revision, _owned.GetValueOrDefault(actor.Id)?.Registration, menu);
            return revision;
        }
    }
    private void Replace(PlayerSnapshot actor, string title, IReadOnlyCollection<MenuOption> options, long revision)
    {
        lock (_gate)
        {
            if (!Current(actor) || !_owned.TryGetValue(actor.Id, out var state) || state.Revision != revision) return;
            _menus.TryGetOpenMenu(actor.Id, out var current);
            if (!ReferenceEquals(current, state.Expected)) return;
            state.Registration?.Dispose();
            var menu = new MenuDefinition(new("ano.admin.menu." + actor.Id.SteamId64), title, options);
            _owned[actor.Id] = new(actor, revision, _menus.Register(Owner, menu), menu);
            _menus.Open(actor.Id, menu.Id);
        }
    }
    private void Remove(PlayerSnapshot player)
    {
        lock (_gate)
            if (_owned.TryGetValue(player.Id, out var state) && state.Player.SessionId == player.SessionId)
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
            foreach (var registration in _registrations.AsEnumerable().Reverse()) registration.Dispose();
            foreach (var state in _owned.Values) state.Registration?.Dispose();
            _owned.Clear();
        }
    }
    private sealed record State(PlayerSnapshot Player, long Revision, IDisposable? Registration, MenuDefinition? Expected);
}
