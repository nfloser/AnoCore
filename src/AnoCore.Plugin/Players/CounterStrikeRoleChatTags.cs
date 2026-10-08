using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Entities;

namespace AnoCore.Plugin.Players;

public static class CounterStrikeRoleChatTags
{
    // Runs in the native say hook on the server thread. Use actual group assignments:
    // PlayerInGroup treats domain-root permissions as membership in other groups.
    public static string? Resolve(RoleChatTagModule module, PlayerSnapshot player)
    {
        var data = AdminManager.GetPlayerAdminData(new SteamID(player.Id.SteamId64));
        return module.Resolve(player.Id, data is null ? Array.Empty<string>() : data.Groups, player.Team);
    }
}
