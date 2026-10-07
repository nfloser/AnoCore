using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Menus;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using Microsoft.Extensions.Logging;

namespace AnoCore.Plugin.Menus;

public sealed class CounterStrikeMenuPresenter : IDisposable
{
    private readonly object _gate = new();
    private readonly BasePlugin _plugin;
    private readonly IMenuService _menus;
    private readonly ILogger _logger;
    private readonly Dictionary<PlayerId, RenderedMenu> _renderedMenus = [];
    private readonly Dictionary<PlayerId, long> _presentedRevisions = [];
    private readonly MenuService? _observableMenus;
    private readonly IPlayerRegistry? _players;
    private int _disposed;

    public CounterStrikeMenuPresenter(BasePlugin plugin, IMenuService menus, ILogger logger, IPlayerRegistry? players = null)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _menus = menus ?? throw new ArgumentNullException(nameof(menus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _players = players;
        _observableMenus = menus as MenuService;
        if (_observableMenus is not null && players is not null) _observableMenus.Changed += OnMenusChanged;
    }

    private void OnMenusChanged(PlayerId playerId)
    {
        _players!.TryGet(playerId, out var captured);
        Server.NextWorldUpdate(() =>
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            if (_observableMenus!.GetOpenRevision(playerId) == 0)
                lock (_gate) _presentedRevisions.Remove(playerId);
            Reconcile();
            if (captured is not { IsConnected: true } || !_players.TryGet(playerId, out var current)
                || current is not { IsConnected: true } || current.SessionId != captured.SessionId) return;
            var player = Utilities.GetPlayers().FirstOrDefault(item => item.IsValid && item.SteamID == playerId.SteamId64);
            OpenIfChanged(player);
        });
    }

    public bool OpenIfChanged(CCSPlayerController? player)
    {
        if (!TryGetPlayerId(player, out var id)) return false;
        var revision = _observableMenus?.GetOpenRevision(id) ?? 0;
        lock (_gate)
            if (revision != 0 && _presentedRevisions.GetValueOrDefault(id) == revision) return false;
        return Open(player);
    }

    public bool Open(CCSPlayerController? player)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        if (!TryGetPlayerId(player, out var playerId) || player is null)
        {
            return false;
        }

        if (!_menus.TryGetOpenMenu(playerId, out var definition) || definition is null)
        {
            return false;
        }

        var menu = new CenterHtmlMenu(definition.Title, _plugin)
        {
            PostSelectAction = PostSelectAction.Nothing,
            ExitButton = true,
        };

        IMenuInstance? openedInstance = null;
        foreach (var option in definition.Options)
        {
            menu.AddMenuOption(option.Label, (controller, menuOption) =>
            {
                _ = menuOption;
                _ = SelectAsync(controller, playerId, definition, option, openedInstance);
            });
        }

        menu.Open(player);
        openedInstance = MenuManager.GetActiveMenu(player);
        if (openedInstance is not null)
        {
            lock (_gate)
            {
                _renderedMenus[playerId] = new RenderedMenu(definition.Id, openedInstance);
                _presentedRevisions[playerId] = _observableMenus?.GetOpenRevision(playerId) ?? 0;
            }
        }

        return true;
    }

    public void Reconcile()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        KeyValuePair<PlayerId, RenderedMenu>[] rendered;
        lock (_gate)
        {
            rendered = _renderedMenus.ToArray();
        }

        var controllers = Utilities.GetPlayers()
            .Where(player => player.IsValid && player.SteamID != 0)
            .GroupBy(player => player.SteamID)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var pair in rendered)
        {
            if (!controllers.TryGetValue(pair.Key.SteamId64, out var player))
            {
                RemoveTracked(pair.Key, pair.Value);
                continue;
            }

            var activeInstance = MenuManager.GetActiveMenu(player);
            if (!ReferenceEquals(activeInstance, pair.Value.Instance))
            {
                RemoveTracked(pair.Key, pair.Value);
                continue;
            }

            if (_menus.TryGetOpenMenu(pair.Key, out var logicalMenu)
                && logicalMenu is not null
                && logicalMenu.Id == pair.Value.MenuId)
            {
                continue;
            }

            MenuManager.CloseActiveMenu(player);
            RemoveTracked(pair.Key, pair.Value);
        }
    }

    private async Task SelectAsync(
        CCSPlayerController player, PlayerId playerId, MenuDefinition definition,
        MenuOption option, IMenuInstance? instance)
    {
        try
        {
            if (!IsCurrent(player, playerId, instance))
                return;
            var result = await _menus.SelectAsync(playerId, definition, option.Id)
                .ConfigureAwait(false);
            Server.NextFrame(() =>
            {
                if (!IsCurrent(player, playerId, instance))
                    return;
                Reconcile();
                if (!result.Accepted)
                {
                    if (!string.IsNullOrWhiteSpace(result.Error))
                    {
                        player.PrintToChat(result.Error);
                    }

                    return;
                }

                if (option.KeepOpen && _menus.TryGetOpenMenu(playerId, out _))
                {
                    Open(player);
                }
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "AnoCore menu selection failed for player {PlayerId} and option {OptionId}.", playerId, option.Id);
        }
    }

    private bool IsCurrent(
        CCSPlayerController player, PlayerId playerId, IMenuInstance? instance)
    {
        if (Volatile.Read(ref _disposed) != 0 || instance is null || !TryGetPlayerId(player, out var currentId)
            || currentId != playerId
            || !ReferenceEquals(MenuManager.GetActiveMenu(player), instance))
            return false;
        lock (_gate)
            return _renderedMenus.TryGetValue(playerId, out var rendered)
                && ReferenceEquals(rendered.Instance, instance);
    }

    private void RemoveTracked(PlayerId playerId, RenderedMenu expected)
    {
        lock (_gate)
        {
            if (_renderedMenus.TryGetValue(playerId, out var current) && ReferenceEquals(current, expected))
            {
                _renderedMenus.Remove(playerId);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_observableMenus is not null && _players is not null) _observableMenus.Changed -= OnMenusChanged;
        foreach (var player in Utilities.GetPlayers().Where(item => item.IsValid && item.SteamID != 0))
        {
            if (!TryGetPlayerId(player, out var id)) continue;
            lock (_gate)
                if (_renderedMenus.TryGetValue(id, out var rendered)
                    && ReferenceEquals(MenuManager.GetActiveMenu(player), rendered.Instance)) MenuManager.CloseActiveMenu(player);
        }
        lock (_gate) { _renderedMenus.Clear(); _presentedRevisions.Clear(); }
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

    private sealed record RenderedMenu(MenuId MenuId, IMenuInstance Instance);
}
