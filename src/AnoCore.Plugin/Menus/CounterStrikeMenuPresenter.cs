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

        float? originalVelocityModifier;
        lock (_gate)
        {
            originalVelocityModifier = _renderedMenus.TryGetValue(playerId, out var previous)
                ? previous.OriginalVelocityModifier
                : ReadVelocityModifier(player);
        }

        var rendered = new RenderedMenu(
            player,
            definition,
            new ScrollMenuCursor(definition.Options.Count, VisibleRows),
            player.Buttons,
            originalVelocityModifier);

        lock (_gate)
            _renderedMenus[playerId] = rendered;

        BlockMovement(player);
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

        foreach (var pair in rendered)
        {
            var player = pair.Value.Player;
            if (!IsSamePlayer(player, pair.Key)
                || !_menus.TryGetOpenMenu(pair.Key, out var logicalMenu)
                || logicalMenu is null
                || logicalMenu.Id != pair.Value.Definition.Id)
            {
                ClosePresentation(pair.Key, pair.Value, IsSamePlayer(player, pair.Key) ? player : null);
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

        foreach (var pair in rendered)
        {
            if (!IsSamePlayer(pair.Value.Player, pair.Key))
                continue;

            RestoreMovement(pair.Value.Player, pair.Value.OriginalVelocityModifier);
            pair.Value.Player.PrintToCenterHtml(" ");
        }
    }

    private void OnTick()
    {
        if (_disposed)
            return;

        KeyValuePair<PlayerId, RenderedMenu>[] rendered;
        lock (_gate)
            rendered = _renderedMenus.ToArray();

        foreach (var pair in rendered)
        {
            var player = pair.Value.Player;
            if (!IsSamePlayer(player, pair.Key))
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

            BlockMovement(player);

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
            else if ((pressed & PlayerButtons.Back) != 0)
                changed |= pair.Value.Cursor.MoveNext();
            else if ((pressed & PlayerButtons.Moveleft) != 0)
                changed |= pair.Value.Cursor.MovePrevious(VisibleRows);
            else if ((pressed & PlayerButtons.Moveright) != 0)
                changed |= pair.Value.Cursor.MoveNext(VisibleRows);

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
                if (!IsCurrent(playerId, rendered) || !IsSamePlayer(player, playerId))
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

    private static void Render(CCSPlayerController player, RenderedMenu rendered)
    {
        if (!player.IsValid)
            return;

        var title = SafeText(rendered.Definition.Title);
        var builder = new System.Text.StringBuilder()
            .Append("<font class='mono-spaced-font'>")
            .Append(title)
            .Append("</font><font class='fontSize-sm stratum-font'>");

        for (var index = rendered.Cursor.StartIndex; index < rendered.Cursor.EndIndexExclusive; index++)
        {
            var label = SafeText(rendered.Definition.Options[index].Label);
            builder.Append("<br>");
            if (index == rendered.Cursor.SelectedIndex)
            {
                builder.Append("<font color='#d6ff5f'>▶ ")
                    .Append(label)
                    .Append("</font>");
            }
            else
            {
                builder.Append(label);
            }
        }

        if (rendered.Definition.Options.Count == 0)
            builder.Append("<br><font color='#aaaaaa'>No options available.</font>");

        builder.Append("</font><br><font class='fontSize-s'>")
            .Append("W/S navigate · A/D page · E select · R close")
            .Append("</font>");

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

        if (player is not null && IsSamePlayer(player, playerId))
        {
            RestoreMovement(player, expected.OriginalVelocityModifier);
            player.PrintToCenterHtml(" ");
        }
    }

    private static bool IsSamePlayer(CCSPlayerController? player, PlayerId playerId)
        => player is { IsValid: true } && player.SteamID == playerId.SteamId64;

    private static float? ReadVelocityModifier(CCSPlayerController player)
    {
        try
        {
            var pawn = player.PlayerPawn.Value;
            return pawn is { IsValid: true } ? pawn.VelocityModifier : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void BlockMovement(CCSPlayerController player)
    {
        try
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn is { IsValid: true })
                pawn.VelocityModifier = 0.0f;
        }
        catch (InvalidOperationException)
        {
            // Pawn can disappear during death/team transitions; the menu remains usable.
        }
    }

    private static void RestoreMovement(CCSPlayerController player, float? velocityModifier)
    {
        if (velocityModifier is null)
            return;

        try
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn is { IsValid: true })
                pawn.VelocityModifier = velocityModifier.Value;
        }
        catch (InvalidOperationException)
        {
            // A replacement pawn will receive normal movement state from the game.
        }
    }

    private static string SafeText(string text)
    {
        var decoded = WebUtility.HtmlDecode(text ?? string.Empty);
        var printable = new string(decoded.Where(character => !char.IsControl(character)).Take(180).ToArray());
        return WebUtility.HtmlEncode(printable);
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
        CCSPlayerController player,
        MenuDefinition definition,
        ScrollMenuCursor cursor,
        PlayerButtons previousButtons,
        float? originalVelocityModifier)
    {
        public CCSPlayerController Player { get; } = player;
        public MenuDefinition Definition { get; } = definition;
        public ScrollMenuCursor Cursor { get; } = cursor;
        public PlayerButtons PreviousButtons { get; set; } = previousButtons;
        public float? OriginalVelocityModifier { get; } = originalVelocityModifier;
        public bool Busy { get; set; }
    }
}
