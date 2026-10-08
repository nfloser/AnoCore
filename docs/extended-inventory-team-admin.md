# Inventory, identity and team administration

Issue #167 continues the extended administration roadmap (#36) with bounded inventory, player-name and team operations.

## Commands

| Command | Permission | Behavior |
| --- | --- | --- |
| `anorename <target> <name>` | `ano.admin.rename` | set one connected player's printable name (1–64 chars) |
| `anostrip <target>` | `ano.admin.strip` | remove all weapons from one living player |
| `anogive <target> <item>` | `ano.admin.give` | give one living player an item from the fixed AnoCore catalog |
| `anoteam <target> <t|ct|spec>` | CSS `@anocore/team` | force T/CT or use normal spectator team change |
| `anoswap <target>` | CSS `@anocore/team` | swap a T/CT player to the opposite team |
| `anohide` / `anostealth` | `ano.admin.hide` | hide the calling connected player by leaving active teams |

Inventory and rename commands reuse the shared AnoCore permission, self-target, immunity and current-session gateway. Team commands use CSS targeting and immunity as described below. Hide is intentionally self-only and performs a second permission check in the executor in addition to the command registry.

## Fixed item catalog

`anogive` never forwards arbitrary entity names. It accepts only exact aliases or class names from `AdminItemCatalog`, covering the supported rifles, SMGs, shotguns, snipers, pistols, grenades, taser, shield and healthshot.

Primary and secondary gives replace the existing slot item. Grenade and healthshot gives respect the server's corresponding ammo limits. The adapter reads those limits through CounterStrikeSharp ConVars rather than hardcoding server policy.

## Native safety

All native writes run on the server update thread and revalidate `PlayerSessionId` immediately before touching a controller or pawn. Rename marks the networked name field changed. Strip uses CounterStrikeSharp's inventory removal API. Team assignment uses forced T/CT switching while spectator uses the normal team-change path.

Hide does not execute arbitrary server commands or toggle global team-selection ConVars. AnoCore kills the current caller session and moves it to `CsTeam.None`; exact visual/scoreboard behavior is a native acceptance item.

## Native acceptance

Verify every command with authorized/unauthorized callers and an immune target. Test quoted rename values, reconnect races, primary/secondary replacement, grenade limits, healthshot limits, strip, T/CT/spec movement, swap rejection for spectators and hide/stealth from a real client. Confirm no arbitrary item class can be created through `anogive`.

## CSS team ownership (#345)

The composed CS2 plugin authorizes only `anoteam` and `anoswap` through the CSS flag `@anocore/team`; it does not grant other `ano.*` permissions. Console is trusted. CSS root retains its normal CSS flag semantics. The original two-argument controller constructor remains compatible for standalone integrations using the old AnoCore permission gateway.

In `addons/counterstrikesharp/configs/admin_groups.json`, append `@anocore/team` to the existing developer and host groups that previously held `@zenith-commands/team`. Keep all other flags, group assignments and immunity values from the server backup. The admin/root group already has CSS root. Do not copy private player IDs into examples or recreate groups unnecessarily.

Example addition to an existing group's `flags` array:

```json
"@anocore/team"
```

CSS names, quoted names and selectors such as `@me`, `#<userid>` and `#<SteamID64>` are resolved on the server thread. Exactly one connected human player must match; broad/ambiguous selectors are rejected. Permission is checked before resolution, then actor/target sessions, flag and CSS immunity are rechecked before mutation. Bots and HLTV are excluded. Moving a living player to spectator first kills the valid pawn and delays the actual team change until the next world update; disconnected/replaced sessions cannot inherit a queued change.

Only individual team changes are requested. No `.t`, `.ct`, `.swap`, `.switch`, `css_*` aliases or global side-swap ConVars are registered. MatchZy's knife-round side selection, match state and normal team-event/roster handlers remain authoritative. A loaded roster may restore its configured team after an admin request: adjust the MatchZy roster/lock policy through MatchZy rather than using AnoCore to bypass it. Success feedback therefore reports a requested change, not a promise to override another plugin.

Native acceptance (not run in the development container): test the existing host/developer groups with and without the new flag; stronger/equal/weaker CSS immunity; console; zero/ambiguous/name/quoted-name/ID/self targets; dead and living spectator moves; T/CT swaps; reconnect, revoked-flag and unload races; and MatchZy practice, loaded-roster and knife winner side-selection behavior. No live configuration is changed by this implementation.
