using AnoCore.Abstractions.Players;
using AnoCore.Plugin.Players;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using Microsoft.Extensions.Logging;

namespace AnoCore.Plugin;

[MinimumApiVersion(374)]
public sealed class AnoCorePlugin : BasePlugin
{
    private AnoEventBus? _eventBus;
    private PlayerRegistry? _players;
    private bool _lifecycleHooksRegistered;

    public override string ModuleName => "AnoCore";

    public override string ModuleDescription => "Modular CS2 server framework for Ano modules.";

    public override string ModuleAuthor => "AnoMeme contributors";

    public override string ModuleVersion => "0.1.0-dev";

    public override void Load(bool hotReload)
    {
        _eventBus = new AnoEventBus();
        _players = new PlayerRegistry(_eventBus);

        RegisterLifecycleHooks();

        AddCommand("css_anostatus", "Show AnoCore runtime status", OnStatus);
        BootstrapConnectedPlayers();
        Logger.LogInformation("AnoCore player lifecycle loaded. Gameplay modules are not yet connected.");
    }

    public override void Unload(bool hotReload)
    {
        RemoveCommand("css_anostatus", OnStatus);
        DeregisterLifecycleHooks();
        _players = null;
        _eventBus = null;
    }

    private void OnStatus(CCSPlayerController? player, CommandInfo command)
    {
        command.ReplyToCommand(
            $"[ANO] AnoCore {ModuleVersion}; tracked humans: {_players?.OnlinePlayers.Count ?? 0}; "
            + "player lifecycle active; gameplay modules not connected.");
    }

    private void RegisterLifecycleHooks()
    {
        if (_lifecycleHooksRegistered)
        {
            return;
        }

        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        _lifecycleHooksRegistered = true;
    }

    private void DeregisterLifecycleHooks()
    {
        if (!_lifecycleHooksRegistered)
        {
            return;
        }

        DeregisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        DeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        DeregisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
        DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        DeregisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        _lifecycleHooksRegistered = false;
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo _)
    {
        TrackConnection(@event.Userid, "player_connect_full");
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo _)
    {
        if (_players is null || @event.Xuid == 0)
        {
            return HookResult.Continue;
        }

        PlayerId id;
        try
        {
            id = new PlayerId(@event.Xuid);
        }
        catch (ArgumentOutOfRangeException)
        {
            return HookResult.Continue;
        }

        if (_players.TryGet(id, out var current) && current is not null)
        {
            Observe(
                _players.DisconnectAsync(id, current.SessionId, DateTimeOffset.UtcNow).AsTask(),
                "player_disconnect");
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo _)
    {
        RefreshNextFrame(@event.Userid, "player_team");
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo _)
    {
        RefreshNextFrame(@event.Userid, "player_spawn");
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo _)
    {
        RefreshNextFrame(@event.Userid, "player_death");
        return HookResult.Continue;
    }

    private void BootstrapConnectedPlayers()
    {
        foreach (var controller in Utilities.GetPlayers())
        {
            TrackConnection(controller, "load_bootstrap");
        }
    }

    private void TrackConnection(CCSPlayerController? controller, string operation)
    {
        if (_players is null
            || !CounterStrikePlayerMapper.TryCreateConnection(
                controller,
                DateTimeOffset.UtcNow,
                out var connection)
            || connection is null)
        {
            return;
        }

        Observe(_players.ConnectAsync(connection).AsTask(), operation);
    }

    private void RefreshNextFrame(CCSPlayerController? controller, string operation)
    {
        if (controller is null)
        {
            return;
        }

        var registry = _players;
        Server.NextFrame(() =>
        {
            if (registry is not null && ReferenceEquals(registry, _players))
            {
                RefreshPlayer(controller, operation);
            }
        });
    }

    private void RefreshPlayer(CCSPlayerController? controller, string operation)
    {
        if (_players is null
            || controller is null
            || !controller.IsValid
            || controller.SteamID == 0)
        {
            return;
        }

        PlayerId id;
        try
        {
            id = new PlayerId(controller.SteamID);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        if (!_players.TryGet(id, out var current)
            || current is null
            || !CounterStrikePlayerMapper.TryCreateUpdate(
                controller,
                current.SessionId,
                DateTimeOffset.UtcNow,
                out var update)
            || update is null)
        {
            return;
        }

        Observe(_players.UpdateAsync(update).AsTask(), operation);
    }

    private void Observe(Task operation, string context)
        => _ = ObserveAsync(operation, context);

    private async Task ObserveAsync(Task operation, string context)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "AnoCore runtime operation {Operation} failed.", context);
        }
    }
}
