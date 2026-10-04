# Inventory, identity and team administration

Issue #167 continues the extended administration roadmap (#36) with bounded inventory, player-name and team operations.

## Commands

| Command | Permission | Behavior |
| --- | --- | --- |
| `anorename <target> <name>` | `ano.admin.rename` | set one connected player's printable name (1–64 chars) |
| `anostrip <target>` | `ano.admin.strip` | remove all weapons from one living player |
| `anogive <target> <item>` | `ano.admin.give` | give one living player an item from the fixed AnoCore catalog |
| `anoteam <target> <t|ct|spec>` | `ano.admin.team` | force T/CT or use normal spectator team change |
| `anoswap <target>` | `ano.admin.swap` | swap a T/CT player to the opposite team |
| `anohide` / `anostealth` | `ano.admin.hide` | hide the calling connected player by leaving active teams |

Mutating target commands reuse the shared permission, self-target, immunity and current-session gateway. Hide is intentionally self-only and performs a second permission check in the executor in addition to the command registry.

## Fixed item catalog

`anogive` never forwards arbitrary entity names. It accepts only exact aliases or class names from `AdminItemCatalog`, covering the supported rifles, SMGs, shotguns, snipers, pistols, grenades, taser, shield and healthshot.

Primary and secondary gives replace the existing slot item. Grenade and healthshot gives respect the server's corresponding ammo limits. The adapter reads those limits through CounterStrikeSharp ConVars rather than hardcoding server policy.

## Native safety

All native writes run on the server update thread and revalidate `PlayerSessionId` immediately before touching a controller or pawn. Rename marks the networked name field changed. Strip uses CounterStrikeSharp's inventory removal API. Team assignment uses forced T/CT switching while spectator uses the normal team-change path.

Hide does not execute arbitrary server commands or toggle global team-selection ConVars. AnoCore kills the current caller session and moves it to `CsTeam.None`; exact visual/scoreboard behavior is a native acceptance item.

## Native acceptance

Verify every command with authorized/unauthorized callers and an immune target. Test quoted rename values, reconnect races, primary/secondary replacement, grenade limits, healthshot limits, strip, T/CT/spec movement, swap rejection for spectators and hide/stealth from a real client. Confirm no arbitrary item class can be created through `anogive`.
