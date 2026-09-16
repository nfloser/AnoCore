using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Players;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using Microsoft.Extensions.Logging;

namespace AnoCore.Plugin.Menus;

public sealed class CounterStrikeMenuPresenter
{
    private readonly BasePlugin _plugin;
    private readonly IMenuService _menus;
    private readonly ILogger _logger;

    public CounterStrikeMenuPresenter(BasePlugin plugin, IMenuService menus, ILogger logger)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _menus = menus ?? throw new ArgumentNullException(nameof(menus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool Open(CCSPlayerController? player)
    {
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

        foreach (var option in definition.Options)
        {
            menu.AddMenuOption(option.Label, (controller, menuOption) =>
            {
                _ = menuOption;
                _ = SelectAsync(controller, playerId, option);
            });
        }

        menu.Open(player);
        return true;
    }

    private async Task SelectAsync(CCSPlayerController player, PlayerId playerId, MenuOption option)
    {
        try
        {
            var result = await _menus.SelectAsync(playerId, option.Id).ConfigureAwait(false);
            Server.NextFrame(() =>
            {
                if (!player.IsValid)
                {
                    return;
                }

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
                else
                {
                    MenuManager.CloseActiveMenu(player);
                }
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "AnoCore menu selection failed for player {PlayerId} and option {OptionId}.", playerId, option.Id);
        }
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
}
