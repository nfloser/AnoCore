using System.Text.RegularExpressions;
using System.Globalization;
using System.Net;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Modules.Progression;

/// <summary>A read-only menu over the existing progression commands, without another state store.</summary>
public sealed class ProgressionMenuModule : IDisposable
{
    public const string CommandName = "anoprogression";
    private static readonly ModuleId Owner = new("ano.progression.menu");
    private static readonly (string Command, string Label, bool Paged)[] Views =
    [
        ("anoxp", "Lifetime XP and active boost", false),
        ("anolevel", "Lifetime XP and level", false),
        ("anoachievements", "Permanent achievements", true),
        ("anochallenges", "Active challenges", true),
        ("anoseason", "Current season", false),
        ("anoseasons", "Season history", true),
        ("anoseasontopcurrent", "Season leaderboard", true),
    ];
    private readonly object _gate = new();
    private readonly IAnoCommandRegistry _commands;
    private readonly IPlayerRegistry _players;
    private readonly IMenuService _menus;
    private readonly Dictionary<PlayerId, (long Request, IDisposable? Registration)> _owned = [];
    private readonly IDisposable _command;
    private readonly IDisposable _disconnect;
    private readonly CancellationTokenSource _lifetime = new();
    private long _request;
    private int _disposed;

    public ProgressionMenuModule(IAnoCommandRegistry commands, IPlayerRegistry players, IMenuService menus, IAnoEventBus events)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _menus = menus ?? throw new ArgumentNullException(nameof(menus));
        ArgumentNullException.ThrowIfNull(events);
        _command = commands.Register(Owner, new(CommandName, "Open lifetime progression, challenges, achievements and seasons."), OpenAsync);
        try
        {
            _disconnect = events.Subscribe<PlayerDisconnectedEvent>((value, _) =>
            {
                lock (_gate)
                    if (!_players.TryGet(value.Player.Id, out var current) || current is not { IsConnected: true })
                        Remove(value.Player.Id);
                return ValueTask.CompletedTask;
            });
        }
        catch { _command.Dispose(); throw; }
    }

    private ValueTask<CommandResult> OpenAsync(CommandContext context)
    {
        if (context.Caller is null || !_players.TryGet(context.Caller, out var player) || player is null || !Current(player))
            return ValueTask.FromResult(CommandResult.Fail(CommandFailureReason.Forbidden, "A connected player is required."));
        Root(player);
        return ValueTask.FromResult(CommandResult.Ok("[ANO] Progression menu opened."));
    }

    private void Root(PlayerSnapshot player)
    {
        var names = _commands.GetCommands().Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var views = Views.Where(item => names.Contains(item.Command)
            && (item.Command != "anolevel" || !names.Contains("anoxp"))).ToArray();
        var options = views.Select(view => new MenuOption(view.Command, view.Label,
            context => context.PlayerId == player.Id && Current(player)
                ? ViewAsync(player, view, 1, context.CancellationToken) : ValueTask.CompletedTask, keepOpen: true)).ToList();
        if (options.Count == 0) options.Add(Info("empty", "No progression sources are enabled."));
        Replace(player, "Progression", options, Begin(player));
    }

    private async ValueTask ViewAsync(PlayerSnapshot player, (string Command, string Label, bool Paged) view,
        int page, CancellationToken cancellationToken)
    {
        if (!Current(player) || page is < 1 or > 1000) return;
        var request = Begin(player);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var input = "!" + view.Command + (view.Paged ? " " + page.ToString(CultureInfo.InvariantCulture) : "");
        var result = await _commands.ExecuteAsync(input, player.Id, linked.Token).ConfigureAwait(false);
        if (!Current(player)) return;
        var lines = (result.Message ?? "Progression data unavailable.").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var pageHeader = lines.Length > 0 ? Regex.Match(lines[0], @"\b(\d+)/(\d+)$") : Match.Empty;
        var totalPages = pageHeader.Success && int.TryParse(pageHeader.Groups[2].Value, out var total) ? total : page;
        if (view.Paged && pageHeader.Success) lines = lines.Skip(1).ToArray();
        var options = lines.Take(8).Select((line, index) => Info("line" + index, Escape(line))).ToList();
        if (view.Paged && page > 1) options.Add(Navigate("previous", "Previous page", player, view, page - 1));
        if (view.Paged && result.Success && page < totalPages) options.Add(Navigate("next", "Next page", player, view, page + 1));
        options.Add(Navigate("refresh", "Refresh", player, view, page));
        options.Add(new("back", "Back to progression", context =>
        {
            if (context.PlayerId == player.Id && Current(player)) Root(player);
            return ValueTask.CompletedTask;
        }, keepOpen: true));
        Replace(player, view.Label + (view.Paged ? " — page " + page.ToString(CultureInfo.InvariantCulture) + "/" + totalPages.ToString(CultureInfo.InvariantCulture) : ""), options, request);
    }

    private MenuOption Navigate(string id, string label, PlayerSnapshot player,
        (string Command, string Label, bool Paged) view, int page)
        => new(id, label, context => context.PlayerId == player.Id && Current(player)
            ? ViewAsync(player, view, page, context.CancellationToken) : ValueTask.CompletedTask, keepOpen: true);

    private long Begin(PlayerSnapshot player)
    {
        lock (_gate)
        {
            if (!Current(player)) return -1;
            foreach (var id in _owned.Keys.Where(id => !_players.TryGet(id, out var other) || other is not { IsConnected: true }).ToArray()) Remove(id);
            var registration = _owned.GetValueOrDefault(player.Id).Registration;
            var request = checked(++_request);
            _owned[player.Id] = (request, registration);
            return request;
        }
    }

    private void Replace(PlayerSnapshot player, string title, IReadOnlyCollection<MenuOption> options, long request)
    {
        lock (_gate)
        {
            if (!Current(player) || !_owned.TryGetValue(player.Id, out var state) || state.Request != request) return;
            state.Registration?.Dispose();
            var definition = new MenuDefinition(new("ano.progression." + player.Id.SteamId64), title, options);
            var registration = _menus.Register(Owner, definition);
            _owned[player.Id] = (request, registration);
            _menus.Open(player.Id, definition.Id);
        }
    }

    private bool Current(PlayerSnapshot player)
        => Volatile.Read(ref _disposed) == 0 && _players.TryGet(player.Id, out var current)
            && current is { IsConnected: true } && current.SessionId == player.SessionId;

    private static string Escape(string text)
    {
        var safe = new string(text.Where(character => !char.IsControl(character)).Take(160).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "Unavailable" : WebUtility.HtmlEncode(safe);
    }

    private static MenuOption Info(string id, string label) => new(id, label, _ => ValueTask.CompletedTask, keepOpen: true);

    private void Remove(PlayerId player)
    {
        if (_owned.Remove(player, out var state)) state.Registration?.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _lifetime.Cancel();
            _disconnect.Dispose();
            _command.Dispose();
            foreach (var id in _owned.Keys.ToArray()) Remove(id);
        }
    }
}
