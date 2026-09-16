using AnoCore.Abstractions.Players;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace AnoCore.Plugin.Players;

internal static class CounterStrikePlayerMapper
{
    public static bool TryCreateConnection(
        CCSPlayerController? controller,
        DateTimeOffset observedAtUtc,
        out PlayerConnection? connection)
    {
        if (!IsTrackable(controller))
        {
            connection = null;
            return false;
        }

        connection = new PlayerConnection(
            new PlayerId(controller!.SteamID),
            controller.PlayerName,
            MapTeam(controller.Team),
            IsAlive(controller),
            observedAtUtc);

        return true;
    }

    public static bool TryCreateUpdate(
        CCSPlayerController? controller,
        PlayerSessionId sessionId,
        DateTimeOffset observedAtUtc,
        out PlayerStateUpdate? update)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        if (!IsTrackable(controller))
        {
            update = null;
            return false;
        }

        update = new PlayerStateUpdate(
            new PlayerId(controller!.SteamID),
            sessionId,
            controller.PlayerName,
            MapTeam(controller.Team),
            IsAlive(controller),
            observedAtUtc);

        return true;
    }

    private static bool IsTrackable(CCSPlayerController? controller)
        => controller is { IsValid: true, IsBot: false, IsHLTV: false }
            && controller.SteamID != 0;

    private static bool IsAlive(CCSPlayerController controller)
        => controller.PlayerPawn is { IsValid: true, Value.Health: > 0 };

    private static PlayerTeam MapTeam(CsTeam team)
        => team switch
        {
            CsTeam.Spectator => PlayerTeam.Spectator,
            CsTeam.Terrorist => PlayerTeam.Terrorist,
            CsTeam.CounterTerrorist => PlayerTeam.CounterTerrorist,
            _ => PlayerTeam.Unknown,
        };
}
