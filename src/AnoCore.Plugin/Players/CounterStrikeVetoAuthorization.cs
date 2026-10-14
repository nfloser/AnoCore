using AnoCore.Abstractions.Players;
using AnoCore.Modules.AnoVeto;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;

namespace AnoCore.Plugin.Players;

public static class CounterStrikeVetoAuthorization
{
    // Keep the dedicated AnoCore veto flag as the primary capability. Some CSS
    // installations do not resolve non-@custom custom flags consistently, so the
    // existing operational Admin/Dev/Host rights are accepted as a compatibility
    // fallback. This matches the server's intended role policy:
    //   admin -> @css/root
    //   dev   -> @css/map + @css/config
    //   host  -> @css/map + @css/config
    public static bool HasPermission(PlayerSnapshot player)
    {
        var controller = Utilities.GetPlayers().FirstOrDefault(value => value is
        { IsValid: true, IsBot: false, IsHLTV: false, Connected: PlayerConnectedState.Connected }
            && value.SteamID == player.Id.SteamId64);

        if (controller is null)
            return false;

        if (AdminManager.PlayerHasPermissions(controller, SessionBoundVetoVoteService.RequiredFlag)
            || AdminManager.PlayerHasPermissions(controller, "@css/root"))
            return true;

        return AdminManager.PlayerHasPermissions(controller, "@css/map")
            && AdminManager.PlayerHasPermissions(controller, "@css/config");
    }
}
