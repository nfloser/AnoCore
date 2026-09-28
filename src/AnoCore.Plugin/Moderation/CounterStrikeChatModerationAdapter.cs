using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace AnoCore.Plugin.Moderation;

public sealed class CounterStrikeChatModerationAdapter : IDisposable
{
    private readonly BasePlugin _plugin;
    private readonly NativeChatRouter _router;
    private readonly CommandInfo.CommandListenerCallback _listener;
    private int _disposed;

    public CounterStrikeChatModerationAdapter(
        BasePlugin plugin,
        NativeChatRouter router)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _listener = OnChat;

        var publicRegistered = false;
        try
        {
            _plugin.AddCommandListener("say", _listener, HookMode.Pre);
            publicRegistered = true;
            _plugin.AddCommandListener("say_team", _listener, HookMode.Pre);
        }
        catch
        {
            if (publicRegistered)
                _plugin.RemoveCommandListener("say", _listener, HookMode.Pre);
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _plugin.RemoveCommandListener("say_team", _listener, HookMode.Pre);
        _plugin.RemoveCommandListener("say", _listener, HookMode.Pre);
    }

    private HookResult OnChat(
        CCSPlayerController? player,
        CommandInfo command)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return HookResult.Continue;

        if (player is null || !player.IsValid || player.IsBot || player.IsHLTV)
            return HookResult.Continue;

        if (player.SteamID == 0)
            return HookResult.Handled;

        PlayerId playerId;
        try
        {
            playerId = new PlayerId(player.SteamID);
        }
        catch (ArgumentOutOfRangeException)
        {
            return HookResult.Handled;
        }

        var isTeamMessage = string.Equals(
            command.GetArg(0), "say_team", StringComparison.OrdinalIgnoreCase);
        var route = _router.Route(
            playerId,
            NormalizeMessage(command.ArgString),
            isTeamMessage);
        if (!route.ShouldIntercept)
            return HookResult.Continue;

        if (string.IsNullOrWhiteSpace(route.FormattedMessage))
            return HookResult.Handled;

        var recipients = route.Recipients
            .Select(id => id.SteamId64)
            .ToHashSet();
        foreach (var recipient in Utilities.GetPlayers())
        {
            if (recipient.IsValid
                && !recipient.IsBot
                && !recipient.IsHLTV
                && recipients.Contains(recipient.SteamID))
            {
                recipient.PrintToChat(route.FormattedMessage);
            }
        }

        return HookResult.Handled;
    }

    private static string NormalizeMessage(string? value)
    {
        var message = value?.Trim() ?? string.Empty;
        if (message.Length >= 2
            && message[0] == '"'
            && message[^1] == '"')
        {
            message = message[1..^1];
        }

        return message;
    }
}
