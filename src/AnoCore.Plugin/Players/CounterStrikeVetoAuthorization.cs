using AnoCore.Abstractions.Players;
using AnoCore.Modules.AnoVeto;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Entities;

namespace AnoCore.Plugin.Players;

public static class CounterStrikeVetoAuthorization
{
    // Resolve authorization against the SteamID64-backed admin record rather than
    // CCSPlayerController.AuthorizedSteamID. The controller can already be connected
    // while AuthorizedSteamID is temporarily unavailable, which made valid CSS admins
    // fail closed during the new picker pre-authorization step.
    public static bool HasPermission(PlayerSnapshot player)
    {
        var controller = Utilities.GetPlayers().FirstOrDefault(value => value is
        { IsValid: true, IsBot: false, IsHLTV: false, Connected: PlayerConnectedState.Connected }
            && value.SteamID == player.Id.SteamId64);

        if (controller is null)
            return false;

        var steamId = (SteamID)player.Id.SteamId64;

        if (AdminManager.PlayerHasPermissions(steamId, SessionBoundVetoVoteService.RequiredFlag)
            || AdminManager.PlayerHasPermissions(steamId, "@css/root"))
            return true;

        // These are the server's intended AnoVeto management roles.
        if (AdminManager.PlayerInGroup(steamId, "#css/admin")
            || AdminManager.PlayerInGroup(steamId, "#css/dev")
            || AdminManager.PlayerInGroup(steamId, "#css/host"))
            return true;

        // Compatibility fallback if group membership has already been flattened into flags.
        return AdminManager.PlayerHasPermissions(steamId, "@css/map")
            && AdminManager.PlayerHasPermissions(steamId, "@css/config");
    }
}
