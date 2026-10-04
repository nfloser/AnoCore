# Extended player-state administration

Issue #165 is the first package under the extended administration roadmap (#36). It adds session-safe online player state commands while reusing the shared AnoCore target authorization and immunity rules.

## Commands and permissions

| Command | Permission | Bounds / behavior |
| --- | --- | --- |
| `anohealth <target> <value>` | `ano.admin.health` | health 1–1000 |
| `anoarmor <target> <value>` | `ano.admin.armor` | armor 0–1000 |
| `anofreeze <target>` | `ano.admin.freeze` | own and preserve the current movement baseline |
| `anounfreeze <target>` | `ano.admin.unfreeze` | restore the AnoCore-owned movement baseline |
| `anonoclip <target>` | `ano.admin.noclip` | own and preserve the current movement baseline |
| `anowalk <target>` | `ano.admin.walk` | restore the AnoCore-owned movement baseline |
| `anoslay <target>` | `ano.admin.slay` | current living session only |
| `anospeed <target> <percent>` | `ano.admin.speed` | 25–400 percent |
| `anoresetspeed <target>` | `ano.admin.resetspeed` | restore the AnoCore-owned speed baseline |
| `anoblind <target> [alpha]` | `ano.admin.blind` | alpha 0–255; omitted means 255 |
| `anounblind <target>` | `ano.admin.unblind` | restore the AnoCore-owned flash baseline |
| `anogod <target>` | `ano.admin.god` | own and preserve damage handling |
| `anoungod <target>` | `ano.admin.ungod` | restore the AnoCore-owned damage baseline |

Player callers pass the same centralized permission, self-target and immunity checks used by moderation. Server console callers remain allowed. The commands require an online target; an explicit offline SteamID is not accepted for native state changes.

## Session and engine safety

Reversible state is keyed by `PlayerSessionId`, not by slot or SteamID alone. Freeze and noclip intentionally share one movement baseline, so changing from one AnoCore-owned movement override to another does not overwrite the state that existed before AnoCore took ownership.

The CounterStrikeSharp transport queues engine access on the server update thread and rechecks the current registry session immediately before reading or changing the pawn. A reconnect between command handling and the scheduled engine operation therefore fails instead of mutating the replacement session.

Owned reversible state is restored on explicit reset and player death while the current pawn is still addressable. Disconnect and map end deliberately forget ownership without touching a vanishing pawn or obsolete map entity; the engine teardown becomes the boundary there. Plugin unload attempts to restore every remaining owned state before disposing the service. A failed restore keeps that facet owned for a later retry, and global cleanup continues with other sessions before reporting aggregated failures. Native unload timing and exact CS2 field behavior remain part of the real-server acceptance gate.

## Native acceptance

On a disposable server, verify each command with an authorized admin, an unauthorized player and a target with equal/higher immunity. For reversible commands, record the original movement/speed/flash/damage state, apply the override, then test explicit reset and death restoration, disconnect/reconnect and map-change teardown, plus plugin-unload restoration. A stale reconnect or reused slot must never receive an earlier session's action or restoration.
