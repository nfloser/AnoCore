using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace AnoCore.Plugin.Moderation;

public sealed class CounterStrikeChatModerationAdapter : IDisposable
{
    private readonly BasePlugin _plugin;
    private readonly ModerationChatGate _gate;
    private readonly CommandInfo.CommandListenerCallback _listener;
    private int _disposed;

    public CounterStrikeChatModerationAdapter(
        BasePlugin plugin,
        ModerationChatGate gate)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
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
            {
                _plugin.RemoveCommandListener("say", _listener, HookMode.Pre);
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _plugin.RemoveCommandListener("say_team", _listener, HookMode.Pre);
        _plugin.RemoveCommandListener("say", _listener, HookMode.Pre);
    }

    private HookResult OnChat(
        CCSPlayerController? player,
        CommandInfo command)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return HookResult.Continue;
        }

        if (player is null
            || !player.IsValid
            || player.IsBot
            || player.IsHLTV)
        {
            return HookResult.Continue;
        }

        if (player.SteamID == 0)
        {
            return HookResult.Handled;
        }

        PlayerId playerId;
        try
        {
            playerId = new PlayerId(player.SteamID);
        }
        catch (ArgumentOutOfRangeException)
        {
            return HookResult.Handled;
        }

        return _gate.Evaluate(playerId) == ChatInterceptionDecision.Block
            ? HookResult.Handled
            : HookResult.Continue;
    }
}
