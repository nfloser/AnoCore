using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Abstractions.Players;

namespace AnoCore.Runtime.Menus;

/// <summary>Navigation over registered feature menus and existing read commands.</summary>
public sealed class AnoHomeMenuModule : IDisposable
{
    public const string CommandName = "anomenu";
    private static readonly ModuleId Owner = new("core.home-menu");
    private static readonly (string Name, string Label, bool Menu, bool Paged)[] Destinations =
    [
        ("anoadminmenu", "Administration actions", true, false),
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
            _registrations.Add(commands.Register(Owner, new("anomenunavigation", "Open all AnoCore menu destinations."), NavigationAsync));
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
        await DashboardAsync(player, context.CancellationToken).ConfigureAwait(false);
        return CommandResult.Ok("[ANO] Home menu opened.");
    }

    private async ValueTask<CommandResult> NavigationAsync(CommandContext context)
    {
        if (context.Caller is null || !_players.TryGet(context.Caller, out var player)
            || player is not { IsConnected: true } || !Current(player))
            return CommandResult.Fail(CommandFailureReason.Forbidden, "A connected player is required.");
        await RootAsync(player, context.CancellationToken).ConfigureAwait(false);
        return CommandResult.Ok("[ANO] Navigation opened.");
    }

    private async ValueTask DashboardAsync(PlayerSnapshot player, CancellationToken cancellationToken)
    {
        var request = Begin(player);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var descriptors = _commands.GetCommands().ToDictionary(command => command.Name, StringComparer.Ordinal);
        async ValueTask<string> Read(string name)
        {
            if (!descriptors.TryGetValue(name, out var command)
                || !await Allowed(player, command, linked.Token).ConfigureAwait(false))
                return "Unavailable";
            var result = await _commands.ExecuteAsync("!" + name, player.Id, linked.Token).ConfigureAwait(false);
            return result.Success ? result.Message ?? "No data available" : "Unavailable";
        }
        var rank = await Read("anorank").ConfigureAwait(false);
        var xp = await Read(descriptors.ContainsKey("anoxp") ? "anoxp" : "anolevel").ConfigureAwait(false);
        var statistics = await Read("anokda").ConfigureAwait(false);
        var gameplay = await Read("anogamestats").ConfigureAwait(false);
        var playtime = await Read("anoplaytime").ConfigureAwait(false);
        var challenges = await Read("anochallenges").ConfigureAwait(false);
        var lines = challenges.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var previews = lines.Length > 1 ? lines.Skip(1).Take(2).ToArray() : lines;
        var dashboard = new MenuDashboard(Text(player.Name + " | " + Compact(rank)), Text(Compact(xp).Split(" | Gameplay XP boost:")[0]), Text(StatisticsSummary(statistics, gameplay)),
            Text(PlaytimeSummary(playtime)), Text(previews.ElementAtOrDefault(0) ?? "No challenges available"),
            Text(previews.ElementAtOrDefault(1) ?? "No additional challenge"));
        var options = new List<MenuOption>
        {
            Info("dashboard_info_profile", dashboard.Profile),
            Info("dashboard_info_progression", dashboard.Progression),
            Info("dashboard_info_statistics", dashboard.Statistics),
            Info("dashboard_info_playtime", dashboard.Playtime),
            Info("dashboard_info_challenge1", dashboard.Challenge1),
            Info("dashboard_info_challenge2", dashboard.Challenge2),
            new("dashboard_menu", "Open menu", context => Current(player)
                ? RootAsync(player, context.CancellationToken) : ValueTask.CompletedTask, keepOpen: true),
            new("dashboard_refresh", "Refresh dashboard", context => Current(player)
                ? DashboardAsync(player, context.CancellationToken) : ValueTask.CompletedTask, keepOpen: true),
        };
        if (descriptors.TryGetValue("anochallenges", out var challengeCommand)
            && await Allowed(player, challengeCommand, linked.Token).ConfigureAwait(false))
            options.Insert(6, new("dashboard_challenges", "All challenges", context => Current(player)
                ? ViewAsync(player, ("anochallenges", "Challenges", false, true), 1, context.CancellationToken, returnToDashboard: true)
                : ValueTask.CompletedTask, keepOpen: true));
        Replace(player, "Your dashboard", options, request, dashboard);
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
        options.Add(Back(player, dashboard: true));
        Replace(player, "AnoCore", options, request);
    }

    private async ValueTask ViewAsync(PlayerSnapshot player,
        (string Name, string Label, bool Menu, bool Paged) view, int page, CancellationToken cancellationToken, bool returnToDashboard = false)
    {
        var request = Begin(player);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        // The registry rechecks permissions at execution, including after revocation.
        var result = await _commands.ExecuteAsync("!" + view.Name + (view.Paged ? " " + page : ""),
            player.Id, linked.Token).ConfigureAwait(false);
        if (view.Menu && result.Success) return;
        var lines = (result.Message ?? "No data available.").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (view.Paged && result.Success && lines.Count > 0)
        {
            var header = Regex.Match(lines[0], @"^(?:\[ANO\] )?\w+ \d+/(\d+)$");
            if (header.Success && int.TryParse(header.Groups[1].Value, out var count))
            {
                lines.RemoveAt(0);
                for (var next = 2; next <= Math.Min(count, 200); next++)
                {
                    var following = await _commands.ExecuteAsync("!" + view.Name + " " + next, player.Id, linked.Token).ConfigureAwait(false);
                    if (!following.Success) break;
                    lines.AddRange((following.Message ?? "").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Skip(1));
                }
            }
        }
        if (view.Name == "anorating")
        {
            lines = lines.Select(line => Regex.Replace(Compact(line).Split(". Details:")[0].Replace("unscored (provisional)", "Not enough match data"), @"^AnoRating \d+/\d+:\s*", "")).ToList();
        }
        var options = lines.Select((line, index) => Info("line" + index, Text(Compact(line)))).ToList();
        options.Add(Back(player, returnToDashboard));
        Replace(player, view.Label, options, request);
    }

    private static string Compact(string value) => value.Replace("[ANO] ", "", StringComparison.Ordinal);
    private static string StatisticsSummary(string combat, string gameplay)
    {
        static decimal Count(string value, string pattern) => decimal.TryParse(
            Regex.Match(value, pattern).Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 0;
        static string Percent(decimal numerator, decimal denominator) => denominator > 0
            ? (100m * numerator / denominator).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "—";
        var kills = Count(combat, @"(\d+) kill");
        var headshots = Count(gameplay, @"\bHeadshotKill=(\d+)");
        var won = Count(gameplay, @"\bMatchWon=(\d+)");
        var lost = Count(gameplay, @"\bMatchLost=(\d+)");
        var available = gameplay != "Unavailable";
        return Compact(combat) + $" · HS: {(available ? Percent(headshots, kills) : "—")} · Match wins: {(available ? Percent(won, won + lost) : "—")}";
    }

    private static string PlaytimeSummary(string value)
    {
        var match = Regex.Match(value, @"Playtime: ([^;]+); today \(UTC\): ([0-9:.]+)");
        if (!match.Success) return Compact(value).Split("Breakdown:")[0];
        static string Duration(string text) => TimeSpan.TryParse(text.TrimEnd('.'), CultureInfo.InvariantCulture, out var time)
            ? $"{(int)time.TotalDays}d {time.Hours}h {time.Minutes}m" : "Unavailable";
        return $"Playtime: {Duration(match.Groups[1].Value)} · Today: {Duration(match.Groups[2].Value)}";
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
    private MenuOption Back(PlayerSnapshot player, bool dashboard = false) => new("home", dashboard ? "Back to dashboard" : "Back to menu", context =>
        !Current(player) ? ValueTask.CompletedTask : dashboard
            ? DashboardAsync(player, context.CancellationToken) : RootAsync(player, context.CancellationToken), keepOpen: true);
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
    private void Replace(PlayerSnapshot player, string title, IReadOnlyCollection<MenuOption> options, long request, MenuDashboard? dashboard = null)
    {
        lock (_gate)
        {
            if (!Current(player) || !_owned.TryGetValue(player.Id, out var state) || state.Request != request) return;
            _menus.TryGetOpenMenu(player.Id, out var currentMenu);
            if (!ReferenceEquals(currentMenu, state.Expected)) return;
            state.Registration?.Dispose();
            var menu = new MenuDefinition(new("ano.home." + player.Id.SteamId64), title, options, dashboard);
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
