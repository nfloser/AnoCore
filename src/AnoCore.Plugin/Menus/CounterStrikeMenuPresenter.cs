using System.Net;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Menus;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace AnoCore.Plugin.Menus;

public sealed class CounterStrikeMenuPresenter : IDisposable
{
    private const int VisibleRows = 6;

    private readonly object _gate = new();
    private readonly BasePlugin _plugin;
    private readonly IMenuService _menus;
    private readonly ILogger _logger;
    private readonly Dictionary<PlayerId, RenderedMenu> _renderedMenus = [];
    private bool _disposed;

    public CounterStrikeMenuPresenter(BasePlugin plugin, IMenuService menus, ILogger logger)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _menus = menus ?? throw new ArgumentNullException(nameof(menus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _plugin.RegisterListener<Listeners.OnTick>(OnTick);
    }

    public bool Open(CCSPlayerController? player)
    {
        if (_disposed || !TryGetPlayerId(player, out var playerId) || player is null)
            return false;

        if (!_menus.TryGetOpenMenu(playerId, out var definition) || definition is null)
            return false;

        var rendered = new RenderedMenu(
            definition,
            new ScrollMenuCursor(definition.Options.Count, VisibleRows),
            player.Buttons);

        lock (_gate)
            _renderedMenus[playerId] = rendered;

        Render(player, rendered);
        return true;
    }

    public void Reconcile()
    {
        if (_disposed)
            return;

        KeyValuePair<PlayerId, RenderedMenu>[] rendered;
        lock (_gate)
            rendered = _renderedMenus.ToArray();

        var controllers = Utilities.GetPlayers()
            .Where(player => player.IsValid && player.SteamID != 0)
            .GroupBy(player => player.SteamID)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var pair in rendered)
        {
            if (!controllers.TryGetValue(pair.Key.SteamId64, out var player)
                || !_menus.TryGetOpenMenu(pair.Key, out var logicalMenu)
                || logicalMenu is null
                || logicalMenu.Id != pair.Value.Definition.Id)
            {
                ClosePresentation(pair.Key, pair.Value, player: controllers.GetValueOrDefault(pair.Key.SteamId64));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _plugin.RemoveListener<Listeners.OnTick>(OnTick);

        KeyValuePair<PlayerId, RenderedMenu>[] rendered;
        lock (_gate)
        {
            rendered = _renderedMenus.ToArray();
            _renderedMenus.Clear();
        }

        var controllers = Utilities.GetPlayers()
            .Where(player => player.IsValid && player.SteamID != 0)
            .GroupBy(player => player.SteamID)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var pair in rendered)
        {
            if (controllers.TryGetValue(pair.Key.SteamId64, out var player))
                player.PrintToCenterHtml(" ");
        }
    }

    private void OnTick()
    {
        if (_disposed)
            return;

        KeyValuePair<PlayerId, RenderedMenu>[] rendered;
        lock (_gate)
            rendered = _renderedMenus.ToArray();

        if (rendered.Length == 0)
            return;

        var controllers = Utilities.GetPlayers()
            .Where(player => player.IsValid && player.SteamID != 0)
            .GroupBy(player => player.SteamID)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var pair in rendered)
        {
            if (!controllers.TryGetValue(pair.Key.SteamId64, out var player))
            {
                ClosePresentation(pair.Key, pair.Value, null);
                continue;
            }

            if (!_menus.TryGetOpenMenu(pair.Key, out var logicalMenu)
                || logicalMenu is null
                || logicalMenu.Id != pair.Value.Definition.Id)
            {
                ClosePresentation(pair.Key, pair.Value, player);
                continue;
            }

            var buttons = player.Buttons;
            var pressed = buttons & ~pair.Value.PreviousButtons;
            pair.Value.PreviousButtons = buttons;

            if ((pressed & PlayerButtons.Reload) != 0)
            {
                _menus.Close(pair.Key);
                ClosePresentation(pair.Key, pair.Value, player);
                continue;
            }

            var changed = false;
            if ((pressed & PlayerButtons.Forward) != 0)
                changed |= pair.Value.Cursor.MovePrevious();
            if ((pressed & PlayerButtons.Back) != 0)
                changed |= pair.Value.Cursor.MoveNext();

            if ((pressed & PlayerButtons.Use) != 0 && !pair.Value.Busy)
            {
                var selected = pair.Value.Cursor.SelectedIndex;
                if (selected >= 0 && selected < pair.Value.Definition.Options.Count)
                {
                    pair.Value.Busy = true;
                    _ = SelectAsync(player, pair.Key, pair.Value, pair.Value.Definition.Options[selected]);
                }
            }

            if (changed || !pair.Value.Busy)
                Render(player, pair.Value);
        }
    }

    private async Task SelectAsync(
        CCSPlayerController player,
        PlayerId playerId,
        RenderedMenu rendered,
        MenuOption option)
    {
        try
        {
            if (!IsCurrent(playerId, rendered))
                return;

            var result = await _menus.SelectAsync(playerId, rendered.Definition, option.Id)
                .ConfigureAwait(false);

            Server.NextWorldUpdate(() =>
            {
                if (!IsCurrent(playerId, rendered) || !player.IsValid || player.SteamID != playerId.SteamId64)
                    return;

                rendered.Busy = false;

                if (!result.Accepted)
                {
                    if (!string.IsNullOrWhiteSpace(result.Error))
                        player.PrintToChat(result.Error);
                    Render(player, rendered);
                    return;
                }

                if (option.KeepOpen && _menus.TryGetOpenMenu(playerId, out var current) && current is not null)
                {
                    Open(player);
                    return;
                }

                ClosePresentation(playerId, rendered, player);
            });
        }
        catch (Exception exception)
        {
            rendered.Busy = false;
            _logger.LogError(
                exception,
                "AnoCore menu selection failed for player {PlayerId} and option {OptionId}.",
                playerId,
                option.Id);
        }
    }

    private void Render(CCSPlayerController player, RenderedMenu rendered)
    {
        if (_disposed || !player.IsValid)
            return;

        var title = WebUtility.HtmlEncode(rendered.Definition.Title);
        var builder = new System.Text.StringBuilder()
            .Append("<b><font color='yellow'>")
            .Append(title)
            .Append("</font></b><br>");

        for (var index = rendered.Cursor.StartIndex; index < rendered.Cursor.EndIndexExclusive; index++)
        {
            var selected = index == rendered.Cursor.SelectedIndex;
            var label = WebUtility.HtmlEncode(rendered.Definition.Options[index].Label);
            builder.Append(selected
                ? "<font color='yellow'>► </font><font color='green'>"
                : "<font color='white'>  ");
            builder.Append(label).Append("</font><br>");
        }

        if (rendered.Definition.Options.Count == 0)
            builder.Append("<font color='grey'>No options available.</font><br>");

        builder.Append("<br><font color='grey'>W/S navigate · E select · R close</font>");
        player.PrintToCenterHtml(builder.ToString());
    }

    private bool IsCurrent(PlayerId playerId, RenderedMenu expected)
    {
        lock (_gate)
            return !_disposed
                && _renderedMenus.TryGetValue(playerId, out var current)
                && ReferenceEquals(current, expected);
    }

    private void ClosePresentation(PlayerId playerId, RenderedMenu expected, CCSPlayerController? player)
    {
        lock (_gate)
        {
            if (!_renderedMenus.TryGetValue(playerId, out var current) || !ReferenceEquals(current, expected))
                return;
            _renderedMenus.Remove(playerId);
        }

        if (player is { IsValid: true })
            player.PrintToCenterHtml(" ");
    }

    private static bool TryGetPlayerId(CCSPlayerController? player, out PlayerId playerId)
    {
        if (player is null || !player.IsValid || player.SteamID == 0)
        {
            playerId = null!;
            return false;
        }

        try
        {
            playerId = new PlayerId(player.SteamID);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            playerId = null!;
            return false;
        }
    }

    private sealed class RenderedMenu(
        MenuDefinition definition,
        ScrollMenuCursor cursor,
        PlayerButtons previousButtons)
    {
        public MenuDefinition Definition { get; } = definition;
        public ScrollMenuCursor Cursor { get; } = cursor;
        public PlayerButtons PreviousButtons { get; set; } = previousButtons;
        public bool Busy { get; set; }
    }
}
