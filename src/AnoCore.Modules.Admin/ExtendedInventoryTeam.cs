using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public enum ExtendedInventoryTeamOperation
{
    Rename = 1,
    Strip = 2,
    Give = 3,
    SetTeam = 4,
    SwapTeam = 5,
}

public enum AdminItemSlot
{
    Other = 0,
    Primary = 1,
    Secondary = 2,
    Grenade = 3,
}

public sealed record AdminItemDefinition(
    string ClassName,
    string DisplayName,
    AdminItemSlot Slot,
    IReadOnlyList<string> Aliases);

public static class AdminItemCatalog
{
    private static readonly IReadOnlyList<AdminItemDefinition> Items =
    [
        Item("weapon_m4a1_silencer", "M4A1-S", AdminItemSlot.Primary, "m4a1s", "m4s"),
        Item("weapon_ssg08", "SSG 08", AdminItemSlot.Primary, "ssg", "scout", "ssg08"),
        Item("weapon_sg556", "SG 556", AdminItemSlot.Primary, "sg556", "sg"),
        Item("weapon_scar20", "SCAR-20", AdminItemSlot.Primary, "scar20", "scar"),
        Item("weapon_nova", "Nova", AdminItemSlot.Primary, "nova"),
        Item("weapon_mp9", "MP9", AdminItemSlot.Primary, "mp9"),
        Item("weapon_mp7", "MP7", AdminItemSlot.Primary, "mp7"),
        Item("weapon_sawedoff", "Sawed-Off", AdminItemSlot.Primary, "sawedoff"),
        Item("weapon_negev", "Negev", AdminItemSlot.Primary, "negev"),
        Item("weapon_mag7", "MAG-7", AdminItemSlot.Primary, "mag7", "mag"),
        Item("weapon_bizon", "PP-Bizon", AdminItemSlot.Primary, "bizon"),
        Item("weapon_xm1014", "XM1014", AdminItemSlot.Primary, "xm1014", "xm"),
        Item("weapon_ump45", "UMP-45", AdminItemSlot.Primary, "ump45", "ump"),
        Item("weapon_mp5sd", "MP5-SD", AdminItemSlot.Primary, "mp5sd", "mp5"),
        Item("weapon_p90", "P90", AdminItemSlot.Primary, "p90"),
        Item("weapon_mac10", "MAC-10", AdminItemSlot.Primary, "mac10", "mac"),
        Item("weapon_m4a1", "M4A1", AdminItemSlot.Primary, "m4a1", "m4"),
        Item("weapon_m249", "M249", AdminItemSlot.Primary, "m249"),
        Item("weapon_galilar", "Galil AR", AdminItemSlot.Primary, "galilar", "galil"),
        Item("weapon_g3sg1", "G3SG1", AdminItemSlot.Primary, "g3sg1"),
        Item("weapon_famas", "FAMAS", AdminItemSlot.Primary, "famas"),
        Item("weapon_awp", "AWP", AdminItemSlot.Primary, "awp"),
        Item("weapon_aug", "AUG", AdminItemSlot.Primary, "aug"),
        Item("weapon_ak47", "AK-47", AdminItemSlot.Primary, "ak47", "ak"),

        Item("weapon_revolver", "Revolver", AdminItemSlot.Secondary, "revolver"),
        Item("weapon_cz75a", "CZ75-Auto", AdminItemSlot.Secondary, "cz75a", "cz"),
        Item("weapon_usp_silencer", "USP-S", AdminItemSlot.Secondary, "usp_silencer", "usp"),
        Item("weapon_p250", "P250", AdminItemSlot.Secondary, "p250"),
        Item("weapon_hkp2000", "P2000", AdminItemSlot.Secondary, "hkp2000", "hkp"),
        Item("weapon_tec9", "Tec-9", AdminItemSlot.Secondary, "tec9", "tec"),
        Item("weapon_glock", "Glock-18", AdminItemSlot.Secondary, "glock"),
        Item("weapon_fiveseven", "Five-SeveN", AdminItemSlot.Secondary, "fiveseven"),
        Item("weapon_elite", "Dual Berettas", AdminItemSlot.Secondary, "elite"),
        Item("weapon_deagle", "Desert Eagle", AdminItemSlot.Secondary, "deagle"),

        Item("weapon_taser", "Taser", AdminItemSlot.Other, "taser"),
        Item("weapon_shield", "Shield", AdminItemSlot.Other, "shield"),
        Item("weapon_healthshot", "Healthshot", AdminItemSlot.Other, "healthshot"),

        Item("weapon_flashbang", "Flashbang", AdminItemSlot.Grenade, "flashbang", "flash"),
        Item("weapon_smokegrenade", "Smoke Grenade", AdminItemSlot.Grenade, "smokegrenade", "smoke"),
        Item("weapon_molotov", "Molotov", AdminItemSlot.Grenade, "molotov"),
        Item("weapon_hegrenade", "HE Grenade", AdminItemSlot.Grenade, "hegrenade", "grenade"),
        Item("weapon_incgrenade", "Incendiary Grenade", AdminItemSlot.Grenade, "incgrenade"),
        Item("weapon_decoy", "Decoy Grenade", AdminItemSlot.Grenade, "decoy"),
        Item("weapon_tagrenade", "Tactical Grenade", AdminItemSlot.Grenade, "tagrenade"),
    ];

    private static readonly IReadOnlyDictionary<string, AdminItemDefinition> Lookup =
        BuildLookup();

    public static IReadOnlyList<AdminItemDefinition> All => Items;

    public static bool TryResolve(
        string value,
        out AdminItemDefinition? item)
    {
        item = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Lookup.TryGetValue(
            value.Trim().ToLowerInvariant(),
            out item);
    }

    private static AdminItemDefinition Item(
        string className,
        string displayName,
        AdminItemSlot slot,
        params string[] aliases)
        => new(
            className,
            displayName,
            slot,
            aliases.Select(alias => alias.ToLowerInvariant()).ToArray());

    private static IReadOnlyDictionary<string, AdminItemDefinition> BuildLookup()
    {
        var lookup = new Dictionary<string, AdminItemDefinition>(
            StringComparer.Ordinal);

        foreach (var item in Items)
        {
            lookup.Add(item.ClassName.ToLowerInvariant(), item);
            foreach (var alias in item.Aliases)
            {
                lookup.Add(alias, item);
            }
        }

        return lookup;
    }
}

public interface IExtendedInventoryTeamTransport
{
    ValueTask RenameAsync(
        PlayerSnapshot player,
        string name,
        CancellationToken cancellationToken = default);

    ValueTask StripAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default);

    ValueTask<bool> GiveAsync(
        PlayerSnapshot player,
        AdminItemDefinition item,
        CancellationToken cancellationToken = default);

    ValueTask SetTeamAsync(
        PlayerSnapshot player,
        PlayerTeam team,
        CancellationToken cancellationToken = default);

    ValueTask HideAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default);
}
