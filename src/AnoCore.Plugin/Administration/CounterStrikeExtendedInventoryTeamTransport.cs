using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace AnoCore.Plugin.Administration;

public sealed class CounterStrikeExtendedInventoryTeamTransport
    : IExtendedInventoryTeamTransport
{
    private readonly IPlayerRegistry _players;

    public CounterStrikeExtendedInventoryTeamTransport(IPlayerRegistry players)
        => _players = players ?? throw new ArgumentNullException(nameof(players));

    public ValueTask RenameAsync(
        PlayerSnapshot player,
        string name,
        CancellationToken cancellationToken = default)
        => RunOnServerThreadAsync(
            player,
            (controller, _) =>
            {
                controller.PlayerName = name;
                Utilities.SetStateChanged(
                    controller,
                    "CBasePlayerController",
                    "m_iszPlayerName");
                return true;
            },
            cancellationToken).AsVoid();

    public ValueTask StripAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default)
        => RunOnServerThreadAsync(
            player,
            (controller, _) =>
            {
                controller.RemoveWeapons();
                return true;
            },
            cancellationToken).AsVoid();

    public ValueTask<bool> GiveAsync(
        PlayerSnapshot player,
        AdminItemDefinition item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return RunOnServerThreadAsync(
            player,
            (controller, pawn) =>
            {
                if (!CanGive(pawn, item))
                {
                    return false;
                }

                switch (item.Slot)
                {
                    case AdminItemSlot.Primary:
                        controller.RemoveItemBySlot(gear_slot_t.GEAR_SLOT_RIFLE);
                        break;
                    case AdminItemSlot.Secondary:
                        controller.RemoveItemBySlot(gear_slot_t.GEAR_SLOT_PISTOL);
                        break;
                }

                controller.GiveNamedItem(item.ClassName);
                return true;
            },
            cancellationToken);
    }

    public ValueTask SetTeamAsync(
        PlayerSnapshot player,
        PlayerTeam team,
        CancellationToken cancellationToken = default)
        => RunOnServerThreadAsync(
            player,
            (controller, _) =>
            {
                var nativeTeam = team switch
                {
                    PlayerTeam.Spectator => CsTeam.Spectator,
                    PlayerTeam.Terrorist => CsTeam.Terrorist,
                    PlayerTeam.CounterTerrorist => CsTeam.CounterTerrorist,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(team),
                        team,
                        "Only spectator, Terrorist and Counter-Terrorist are supported."),
                };

                if (nativeTeam == CsTeam.Spectator)
                {
                    controller.ChangeTeam(nativeTeam);
                }
                else
                {
                    controller.SwitchTeam(nativeTeam);
                }

                return true;
            },
            cancellationToken).AsVoid();

    public ValueTask HideAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default)
        => RunOnServerThreadAsync(
            player,
            (controller, _) =>
            {
                controller.CommitSuicide(explode: true, force: false);
                controller.ChangeTeam(CsTeam.None);
                return true;
            },
            cancellationToken).AsVoid();

    private static bool CanGive(
        CCSPlayerPawn pawn,
        AdminItemDefinition item)
    {
        if (item.Slot == AdminItemSlot.Grenade)
        {
            var grenadeLimit = GetLimit("ammo_grenade_limit_total", 4);
            var grenades = CountGrenades(pawn);
            if (grenades >= grenadeLimit)
            {
                return false;
            }

            var sameItemCount = CountDesignerName(pawn, item.ClassName);
            if (string.Equals(
                    item.ClassName,
                    "weapon_flashbang",
                    StringComparison.Ordinal))
            {
                return sameItemCount < GetLimit(
                    "ammo_grenade_limit_flashbang",
                    2);
            }

            return sameItemCount < GetLimit(
                "ammo_grenade_limit_default",
                1);
        }

        if (string.Equals(
                item.ClassName,
                "weapon_healthshot",
                StringComparison.Ordinal))
        {
            var services = pawn.WeaponServices;
            if (services is null || services.Ammo.Length <= 20)
            {
                return true;
            }

            return services.Ammo[20] < GetLimit(
                "ammo_item_limit_healthshot",
                4);
        }

        return true;
    }

    private static int CountGrenades(CCSPlayerPawn pawn)
        => CountMatching(
            pawn,
            designerName => designerName is
                "weapon_flashbang"
                or "weapon_smokegrenade"
                or "weapon_molotov"
                or "weapon_hegrenade"
                or "weapon_incgrenade"
                or "weapon_decoy"
                or "weapon_tagrenade");

    private static int CountDesignerName(
        CCSPlayerPawn pawn,
        string expected)
        => CountMatching(
            pawn,
            designerName => string.Equals(
                designerName,
                expected,
                StringComparison.Ordinal));

    private static int CountMatching(
        CCSPlayerPawn pawn,
        Func<string, bool> predicate)
    {
        var services = pawn.WeaponServices;
        if (services is null)
        {
            return 0;
        }

        var count = 0;
        foreach (var weapon in services.MyWeapons)
        {
            if (weapon.IsValid
                && weapon.Value is { IsValid: true } value
                && predicate(value.DesignerName))
            {
                count++;
            }
        }

        return count;
    }

    private static int GetLimit(
        string name,
        int fallback)
    {
        var conVar = ConVar.Find(name);
        return conVar is null
            ? fallback
            : Math.Max(0, conVar.GetPrimitiveValue<int>());
    }

    private ValueTask<T> RunOnServerThreadAsync<T>(
        PlayerSnapshot player,
        Func<CCSPlayerController, CCSPlayerPawn, T> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Server.NextWorldUpdate(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (controller, pawn) = ResolveCurrentPlayer(player);
                completion.TrySetResult(action(controller, pawn));
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        return new ValueTask<T>(completion.Task);
    }

    private (CCSPlayerController Controller, CCSPlayerPawn Pawn)
        ResolveCurrentPlayer(PlayerSnapshot expected)
    {
        if (!_players.TryGet(expected.Id, out var current)
            || current is null
            || !current.IsConnected
            || current.SessionId != expected.SessionId)
        {
            throw new InvalidOperationException(
                "The target player session changed before the engine action ran.");
        }

        var controller = Utilities.GetPlayers().FirstOrDefault(value =>
            value is { IsValid: true, IsBot: false, IsHLTV: false }
            && value.SteamID == expected.Id.SteamId64);

        var pawn = controller?.PlayerPawn.Value;
        if (controller is null
            || pawn is null
            || !pawn.IsValid)
        {
            throw new InvalidOperationException(
                "The target player no longer has a valid Counter-Strike pawn.");
        }

        return (controller, pawn);
    }
}
