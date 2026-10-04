# Positional and revive administration

Issue #166 extends the #36 administration work with the native position-oriented commands that depend on the session-safe target layer from #165.

## Commands

| Command | Permission | Behavior |
| --- | --- | --- |
| `anorespawn <target>` | `ano.admin.respawn` | respawn one connected dead player at the normal game spawn |
| `anorevive <target>` | `ano.admin.revive` | respawn one connected dead player and return the same session to its last recorded death position |
| `anotppos <target> <x> <y> <z>` | `ano.admin.teleport` | teleport one living player to finite bounded world coordinates |
| `anotp <target> <destination>` | `ano.admin.teleport` | teleport one living player to another living player's current position |
| `anobury <target>` | `ano.admin.bury` | move the player down 25 world units |
| `anounbury <target>` | `ano.admin.bury` | move the player up 30 world units |
| `anoslap <target> [damage]` | `ano.admin.slap` | apply 0–1000 damage and a native velocity impulse; omitted damage is zero |

Coordinates are parsed invariantly and rejected when non-finite or outside ±32768. The primary target always passes the shared permission/immunity/current-session gateway. Teleport destinations are resolved independently because they are reference positions rather than mutation targets.

## Death-position lifecycle

The plugin records the current pawn origin in a pre-death game-event hook and keys that value by `PlayerSessionId`. A revive can therefore only consume a death position belonging to that exact connection. Disconnect and map end delete stored positions; a reconnect cannot inherit the previous session's death point.

The native transport revalidates the current session on the CS2 server update thread before reading a position, respawning, teleporting or slapping. Revive performs a native respawn first and schedules the position write only after that step completed.

## Reference behavior

The default bury/unbury offsets and slap behavior intentionally retain the established GPL-compatible reference semantics already attributed in `NOTICE.md`: bury is −25 units, unbury is +30, slap defaults to zero damage and adds a randomized velocity impulse. AnoCore adds stricter coordinate/damage bounds and current-session validation.

## Native acceptance

On a disposable server verify: respawn rejects living targets; revive returns to the exact pre-death position; reconnect removes an old death position; decimals and negative teleport coordinates work; invalid/non-finite/out-of-range coordinates do not move a pawn; target-to-player teleport rechecks both sessions; bury/unbury move by the documented offsets; slap changes health/velocity and kills when damage reaches remaining health. Repeat after map change and hot reload.
