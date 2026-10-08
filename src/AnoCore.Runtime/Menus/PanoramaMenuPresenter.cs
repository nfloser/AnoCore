using System.Net;
using System.Text.RegularExpressions;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Runtime.Menus;

/// <summary>Projects existing logical menus into one per-player Panorama layout.</summary>
public sealed class PanoramaMenuPresenter : IDisposable
{
    public static readonly CustomHudId HudId = new("ano.menu");
    public const string LayoutResource = "panorama/layout/custom_game/anocore/menu.xml";
    public const int PageSize = 6;
    private readonly object _gate = new();
    private readonly ICustomHudService _hud;
    private readonly IMenuService _menus;
    private readonly IPlayerRegistry _players;
    private readonly IAnoCommandRegistry _commands;
    private readonly Dictionary<PlayerId, Presentation> _open = [];
    private readonly List<IDisposable> _registrations = [];
    private bool _disposed;

    public PanoramaMenuPresenter(ICustomHudService hud, IMenuService menus,
        IPlayerRegistry players, IAnoCommandRegistry commands, IAnoEventBus events)
    {
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
        _menus = menus ?? throw new ArgumentNullException(nameof(menus));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        ArgumentNullException.ThrowIfNull(events);
        try
        {
            _registrations.Add(hud.Register(new ModuleId("core.menu"), new(HudId,
                LayoutResource, "ano_menu_root", Enumerable.Range(0, PageSize)
                    .Select(index => $"ano_menu_row_{index}")
                    .Concat(["ano_menu_back", "ano_menu_previous", "ano_menu_next", "ano_menu_home", "ano_menu_close"])
                    .ToArray(), captureInput: true), ClickAsync));
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

    public bool Open(PlayerId playerId)
    {
        lock (_gate)
        {
            if (_disposed || !_players.TryGet(playerId, out var player) || player is not { IsConnected: true }
                || !_menus.TryGetOpenMenu(playerId, out var menu) || menu is null) return false;
            var page = _open.TryGetValue(playerId, out var old) && old.Player.SessionId == player.SessionId
                && old.Menu.Id == menu.Id && old.Menu.Title == menu.Title ? old.Page : 0;
            var state = new Presentation(player, menu, Math.Min(page, LastPage(menu)));
            _open[playerId] = state;
            Render(state);
            return _hud.Show(playerId, HudId);
        }
    }

    public void Reconcile()
    {
        lock (_gate)
        {
            foreach (var state in _open.Values.ToArray())
            {
                if (!CurrentSession(state) || !_menus.TryGetOpenMenu(state.Player.Id, out var menu) || menu is null)
                    Remove(state.Player.Id);
                else if (!ReferenceEquals(menu, state.Menu)) Open(state.Player.Id);
            }
        }
    }

    public void CloseAll()
    {
        lock (_gate)
        {
            foreach (var id in _open.Keys.ToArray())
            {
                _menus.Close(id);
                Remove(id);
            }
        }
    }

    private async ValueTask ClickAsync(CustomHudClickContext context)
    {
        Presentation state;
        MenuOption? option = null;
        var home = false;
        var back = false;
        lock (_gate)
        {
            if (_disposed || context.HudId != HudId || !_open.TryGetValue(context.PlayerId, out state!)
                || !Current(state)) return;
            if (context.ButtonId == "ano_menu_close")
            {
                _menus.Close(context.PlayerId);
                Remove(context.PlayerId);
                return;
            }
            if (state.Busy) return;
            if (context.ButtonId is "ano_menu_previous" or "ano_menu_next")
            {
                var forward = context.ButtonId == "ano_menu_next";
                option = state.Menu.Options.FirstOrDefault(item => item.Label == (forward ? "Next page" : "Previous page"));
                if (option is null)
                {
                    state.Page = Math.Clamp(state.Page + (forward ? 1 : -1), 0, LastPage(state.Menu));
                    Render(state);
                    return;
                }
            }
            back = context.ButtonId == "ano_menu_back";
            if (back && state.Menu.Dashboard is not null) return;
            home = context.ButtonId == "ano_menu_home";
            if (back)
                option = state.Menu.Options.FirstOrDefault(item => item.Id == "back")
                    ?? state.Menu.Options.FirstOrDefault(item => item.Id == "home");
            if (!home && !back && option is null)
            {
                if (!context.ButtonId.StartsWith("ano_menu_row_", StringComparison.Ordinal)
                    || !int.TryParse(context.ButtonId.AsSpan("ano_menu_row_".Length), out var row)
                    || row is < 0 or >= PageSize) return;
                var index = state.Page * PageSize + row;
                if (index >= Options(state.Menu).Count) return;
                option = Options(state.Menu)[index];
            }
            state.Busy = true;
            _hud.SetClass(context.PlayerId, HudId, "ano_menu_root", "busy", true);
        }
        try
        {
            string? error;
            if (home || (back && option is null))
            {
                var result = await _commands.ExecuteAsync(home ? "!anomenu" : "!anomenunavigation", context.PlayerId, context.CancellationToken).ConfigureAwait(false);
                error = result.Success ? null : result.Message;
            }
            else
            {
                var result = await _menus.SelectAsync(context.PlayerId, state.Menu, option!.Id, context.CancellationToken).ConfigureAwait(false);
                error = result.Accepted ? null : result.Error;
            }
            lock (_gate)
            {
                // Close, replacement, reconnect and unload must win over a delayed callback.
                if (_disposed || !_open.TryGetValue(context.PlayerId, out var active)
                    || !ReferenceEquals(active, state) || !CurrentSession(state)) return;
                state.Busy = false;
                if (_menus.TryGetOpenMenu(context.PlayerId, out _))
                {
                    Open(context.PlayerId);
                    if (error is not null) _hud.SetText(context.PlayerId, HudId, "ano_menu_status", Text(error));
                }
                else Remove(context.PlayerId);
            }
        }
        finally
        {
            lock (_gate)
            {
                if (!_disposed && _open.TryGetValue(context.PlayerId, out var active) && ReferenceEquals(active, state))
                {
                    state.Busy = false;
                    _hud.SetClass(context.PlayerId, HudId, "ano_menu_root", "busy", false);
                }
            }
        }
    }

    private void Render(Presentation state)
    {
        var id = state.Player.Id;
        var dashboard = state.Menu.Dashboard;
        _hud.SetClass(id, HudId, "ano_menu_root", "dashboard", dashboard is not null);
        var values = new[] { dashboard?.Profile, dashboard?.Progression, dashboard?.Statistics,
            dashboard?.Playtime, dashboard?.Challenge1, dashboard?.Challenge2 };
        var panels = new[] { "profile", "progression", "statistics", "playtime", "challenge_0", "challenge_1" };
        for (var index = 0; index < panels.Length; index++)
            _hud.SetText(id, HudId, "ano_dashboard_" + panels[index], Text(values[index] ?? string.Empty));
        _hud.SetText(id, HudId, "ano_menu_title", Text(Regex.Replace(state.Menu.Title, @" — page \d+(?:/\d+)?$", "")));
        _hud.SetText(id, HudId, "ano_menu_status", dashboard is null ? "Select an item or return to Home" : "Personal overview — refresh to update");
        var sourcePage = Regex.Match(state.Menu.Title, @" — page (\d+)/(\d+)$");
        _hud.SetText(id, HudId, "ano_menu_page", sourcePage.Success
            ? $"{sourcePage.Groups[1].Value} / {sourcePage.Groups[2].Value}" : $"{state.Page + 1} / {LastPage(state.Menu) + 1}");
        _hud.SetClass(id, HudId, "ano_menu_root", "busy", false);
        _hud.SetClass(id, HudId, "ano_menu_back", "disabled", dashboard is not null);
        _hud.SetClass(id, HudId, "ano_menu_previous", "disabled", state.Page == 0 && !state.Menu.Options.Any(item => item.Label == "Previous page"));
        _hud.SetClass(id, HudId, "ano_menu_next", "disabled", state.Page == LastPage(state.Menu) && !state.Menu.Options.Any(item => item.Label == "Next page"));
        for (var row = 0; row < PageSize; row++)
        {
            var index = state.Page * PageSize + row;
            var visible = index < Options(state.Menu).Count;
            _hud.SetClass(id, HudId, $"ano_menu_row_{row}", "hidden", !visible);
            _hud.SetText(id, HudId, $"ano_menu_row_{row}_text", visible ? Text(Options(state.Menu)[index].Label) : string.Empty);
        }
    }

    private static IReadOnlyList<MenuOption> Options(MenuDefinition menu) => menu.Options.Where(option =>
        option.Label is not ("Next page" or "Previous page") && option.Id is not ("home" or "back")
        && (menu.Dashboard is null || !option.Id.StartsWith("dashboard_info_", StringComparison.Ordinal))).ToArray();

    private bool CurrentSession(Presentation state)
        => _players.TryGet(state.Player.Id, out var current) && current is { IsConnected: true }
            && current.SessionId == state.Player.SessionId;
    private bool Current(Presentation state)
        => CurrentSession(state) && _menus.TryGetOpenMenu(state.Player.Id, out var menu) && ReferenceEquals(menu, state.Menu);
    private static int LastPage(MenuDefinition menu) => Math.Max(0, (Options(menu).Count - 1) / PageSize);
    private static string Text(string text)
        => new(WebUtility.HtmlDecode(text).Where(character => !char.IsControl(character)).Take(240).ToArray());
    private void RemoveSession(PlayerSnapshot player)
    {
        lock (_gate)
            if (_open.TryGetValue(player.Id, out var state) && state.Player.SessionId == player.SessionId)
            {
                if (_menus.TryGetOpenMenu(player.Id, out var menu) && ReferenceEquals(menu, state.Menu))
                    _menus.Close(player.Id);
                Remove(player.Id);
            }
    }
    private void Remove(PlayerId id)
    {
        _open.Remove(id);
        _hud.Hide(id, HudId);
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            CloseAll();
            _disposed = true;
            foreach (var registration in _registrations) registration.Dispose();
        }
    }
    private sealed class Presentation(PlayerSnapshot player, MenuDefinition menu, int page)
    {
        public PlayerSnapshot Player { get; } = player;
        public MenuDefinition Menu { get; } = menu;
        public int Page { get; set; } = page;
        public bool Busy { get; set; }
    }
}
