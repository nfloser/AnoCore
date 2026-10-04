# CounterStrikeSharp runtime verification

Runtime-facing changes are compiled and unit tested in CI, but native CS2 behavior must also be verified on a real CounterStrikeSharp server before release readiness. CI validates Panorama source structure but cannot execute Valve ResourceCompiler or render a client HUD.

## Automated coverage

CI verifies:

- `AnoEventBus` subscriber ordering and event-type isolation;
- independent and idempotent unsubscription;
- subscriber failure isolation with aggregated failures;
- cancellation behavior;
- subscription mutation during publication;
- player connect/reconnect/update/disconnect state semantics;
- compilation against the pinned CounterStrikeSharp API.

## Manual integration checklist

Use a disposable/non-production CS2 server with the pinned CounterStrikeSharp API version.

1. Install the built AnoCore plugin and start the server with no players connected.
2. Confirm AnoCore loads once and the server log contains no plugin exceptions. Run `css_anostatus`; expect zero tracked humans on an empty server.
3. Join with one human Steam account. Run `!anostatus` and confirm one player connection is tracked and no duplicate lifecycle errors occur.
4. Change between Spectator, Terrorist and Counter-Terrorist. Confirm the server remains stable and the player snapshot follows the resulting team.
5. Spawn and die. Confirm alive state follows the game state after the next frame.
6. Disconnect and reconnect the same Steam account. Confirm the reconnect creates a new session and delayed callbacks from the old session do not remove the new session.
7. With a player connected, hot-reload AnoCore. Confirm the connected human is bootstrapped once into the fresh registry and no duplicate game-event handlers fire.
8. Manually load AnoCore with already-connected humans and verify `css_anostatus` includes them. Unload AnoCore. Confirm `css_anostatus` is removed and game-event hooks are deregistered and no AnoCore callbacks execute afterward.
9. Start an AnoVeto vote with at least eight configured maps and the compiled AnoCore Panorama addon mounted on the client. Confirm the fullscreen AnoVeto panel renders without CenterHtml, the cursor is captured, all eight map buttons are clickable, and the Close button releases input without casting a vote.
10. With two clients, confirm each client can see the same vote while maintaining independent visibility/input state. Cast a vote from one client and confirm that client's HUD closes and a second vote from the same SteamID is rejected.
11. Disconnect/reconnect during an active vote and confirm slot reuse does not leak the previous occupant's HUD or cursor state; a reconnect of the same eligible SteamID can reopen the current vote with `!anoveto`.
12. Cancel and timeout votes and confirm the HUD disappears and input capture is released for all affected clients. Hot-reload while a HUD is visible and verify the owned `custom_hud_layout` entity is cleaned/recreated without duplicate click callbacks.
13. Complete a vote and verify exactly one real `changelevel` / `host_workshop_map` transition occurs.
14. Repeat load → hot reload → unload twice and inspect logs for duplicate event handling, orphaned HUD entities, stuck cursors or unobserved task failures.
15. With #165 installed, exercise `anohealth`, `anoarmor`, freeze/unfreeze, noclip/walk, slay, speed/reset, blind/unblind and god/ungod against an authorized target. Confirm permission denial and immunity before native mutation.
16. For freeze/noclip, speed, blind and god mode, verify explicit reset plus death, disconnect/reconnect and map-change cleanup. A replacement `PlayerSessionId` or reused slot must not receive the previous session's mutation or restoration.
17. Exercise `anorespawn`, `anorevive`, `anotppos`, `anotp`, `anobury`, `anounbury` and `anoslap`. Verify revive uses the pre-death location only for the current session, teleport rejects invalid coordinates, and reconnect/map change clears old death positions.
18. Exercise `anorename`, `anostrip`, `anogive`, `anoteam`, `anoswap` and `anohide`. Verify the item catalog rejects arbitrary entity names, inventory limits are honored, spectator/team transitions behave correctly and a stale/reconnected session is never mutated.

## Release gate

This checklist is a verification procedure, not a claim that native-server testing has already happened. The first distributable release remains blocked until the end-to-end acceptance issue records a passing real-server run.
