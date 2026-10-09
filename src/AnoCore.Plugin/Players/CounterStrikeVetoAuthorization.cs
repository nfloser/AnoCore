using AnoCore.Abstractions.Players;
using AnoCore.Modules.AnoVeto;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;

namespace AnoCore.Plugin.Players;

public static class CounterStrikeVetoAuthorization
{
    // Invoked only by the scoped service's world-update callback. CSS owns root
    // semantics and loaded flag assignments; visual role tags grant no authority.
    public static bool HasPermission(PlayerSnapshot player)
    {
        var controller = Utilities.GetPlayers().FirstOrDefault(value => value is
        { IsValid: true, IsBot: false, IsHLTV: false, Connected: PlayerConnectedState.Connected }
            && value.SteamID == player.Id.SteamId64);
        return controller is not null && AdminManager.PlayerHasPermissions(controller, SessionBoundVetoVoteService.RequiredFlag);
    }
}
