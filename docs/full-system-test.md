# Full-system disposable-server test

This runbook belongs to issue #157 and the `integration/157-full-system-test` branch. It is intentionally stricter than a normal feature smoke test: install one exact CI artifact and exercise the integrated native stacks together before any of the previously gated draft work is promoted to production.

## Use one exact build

1. Open the successful CI run for the full-system integration PR.
2. Download `AnoCore-development`.
3. Record `BUILD-COMMIT.txt`; do not mix files from another run or branch.
4. Back up the existing AnoCore plugin directory and the AnoCore MariaDB database.
5. Install the complete `artifacts/plugins/AnoCore` directory as described in `deployment.md`.
6. Keep the bundled `ui/AnoCore` sources with the test record. Compile/deliver the CustomHud Panorama assets as described in `custom-hud.md`.
7. Configure a dedicated test MariaDB and restart the server.

A green CI run proves compilation, automated tests, MariaDB integration, package checks and source-level Panorama validation. It does not prove CounterStrikeSharp callbacks, client rendering or game-engine state.

## Startup and lifecycle

Record pass/fail plus relevant server-log lines for each item.

- Plugin loads without loader exceptions.
- `css_anostatus` reports `ready`.
- Join with two real clients; tracked-human count changes correctly.
- Bots/HLTV are not treated as normal players.
- Disconnect/reconnect each client and verify no duplicate state or stale callbacks.
- Hot reload once with players connected, then repeat status and one command from each feature group.
- Change map and verify the plugin, HUD transport and persisted data remain usable.
- Unload/reload and verify commands/listeners are not duplicated.

## Authorization and moderation

Use at least one authorized admin, one lower-immunity player and one unauthorized player.

- Permission denial happens before privileged mutations.
- Ban a player, reconnect them and confirm connect-ban removal from the server.
- Unban/expiry allows connection again.
- Kick and silent-kick disconnect the intended current session and write audit data.
- Issue, inspect and clear warnings; history remains consistent after reconnect/restart.
- Gag blocks public/team chat without a database lookup on the chat hot path.
- Mute/silence blocks the expected voice routing and restores it after removal.
- Reconnect while a restriction is active; no stale session inherits/escapes the wrong state.

## Extended administration

Use the same admin, lower-immunity target and unauthorized player as above. See [player state](extended-admin.md), [positions](extended-position-admin.md) and [inventory/teams](extended-inventory-team-admin.md) for exact arguments and permissions.

- Exercise health/armor, freeze/unfreeze, noclip/walk, slay, speed/reset, blind/unblind and god/ungod.
- Check explicit resets, death, disconnect/reconnect, map change and unload. Reversible effects must not reach a replacement session or reused slot.
- Exercise respawn, revive-at-death-position, coordinate/player teleport, bury/unbury and slap. Reject non-finite/out-of-range coordinates; clear death positions on reconnect/map change.
- Exercise rename, strip, give, team, swap and self-hide. Reject arbitrary entity names and control characters; honor grenade/healthshot limits and spectator swap rejection.
- Repeat permission and immunity denial for each command family; verify no native mutation occurs.
- Repeat representative commands after hot reload and ensure the existing rank, chat, settings and moderation commands still work.

## Protected server controls and same-network inspection

See [protected server controls](protected-server-controls.md) for allow-list configuration. CVar/server mutations are disabled by default.

- Allow-list one harmless ConVar and server command; check admin and server-console execution plus unauthorized-player denial.
- Reject unlisted names, built-in denied names and separator/control-character arguments before engine side effects.
- Verify audit rows contain the control name and redact supplied values/arguments.
- With two human clients sharing an external address and one on a different address, verify `anosameip` groups only the matching clients, with bounded output and no raw IP/port.
- Repeat after plugin reload and ensure no command registration is duplicated.

## Statistics, playtime and ranks

Use controlled kills/deaths/assists and at least one reconnect.

- Playtime advances through checkpoints and survives reconnect/restart.
- Duplicate/late session callbacks do not double-count.
- Kills, deaths and assists persist and `anokda` reflects the expected totals.
- Fire a controlled set of shots and body/head hits; `anodetailstats` reports persisted shots/hits/damage and `anohitgroups` reports the expected bounded hitgroup buckets.
- Repeat detail queries with map and weapon filters. Team/self damage must not inflate the default offensive totals; world/unattributed damage must not be credited to a player attacker.
- Kill/death/assist toplists use stable ordering for ties.
- Rank score, placement and progress match the configured thresholds.
- Administrative give/take/set/reset operations persist and are audited.
- Rank transition notifications are emitted only for real threshold transitions.
- Rank menu navigation works for first/middle/last pages.
- Restart and re-run representative queries to confirm persistence.

## Chat formatting and player choices

- Public and team messages are routed once, not duplicated by native chat.
- Commands beginning with the supported command prefixes are still commands rather than reformatted chat.
- Rank tags update after combat/admin score changes.
- Configured colors render without leaking color state into following segments.
- A player sees only chat tags they are currently authorized to select.
- Select a tag, reconnect/restart, and verify persistence.
- Remove the permission after opening the menu; a stale menu callback must not apply the tag.
- Reset to the rank/default tag and verify the fallback.

## Player settings

The rank stack registers the real `rank.notifications` bool toggle with default `on`; no test-only registration is required.

- `anosettings` shows `rank.notifications=on` for a player without an override.
- Cross a rank threshold and confirm the promotion/demotion message is delivered while the option is on.
- `anotoggle rank.notifications off` persists and suppresses the next real combat/admin rank-transition message without suppressing the underlying score/rank change.
- `anotoggle rank.notifications default` removes the override and restores the enabled default.
- `anosettingsmenu` renders bounded navigation and can toggle/reset an option.
- Console, disconnected/stale sessions and unknown keys cannot mutate another player's state.
- Reconnect/restart preserves stored overrides and uses defaults after reset.
- Old menu callbacks cannot mutate a replacement menu/session.

## AnoVeto and CustomHud

This is the most client-sensitive part of the run.

- `anoveto` opens the Panorama CustomHud rather than the old CenterHtml vote.
- All eight map choices render and can be clicked.
- Close releases cursor/input without cancelling the shared vote.
- Two clients have independent HUD/cursor state and can each vote only once.
- Disconnect/reconnect/slot reuse does not inherit another session's HUD state.
- Cancel, timeout, completion, map change, hot reload and unload hide/release the HUD.
- The winning configured map resolves to the correct map/Workshop ID and triggers exactly one map transition.
- `anoconfigs` exposes the adopted `anoveto` and `maps` configurations.
- Reload valid map/veto configuration and verify it affects the next vote.
- Reload invalid configuration and verify the last accepted runtime value remains active.
- An already-running vote keeps the policy/catalog snapshot it started with.

## SDK artifact

From the same CI download:

- `sdk/AnoCore.Abstractions.*.nupkg` exists.
- A tiny external module project can restore using only that local package.
- It compiles without referencing Runtime, Plugin, CounterStrikeSharp or MySqlConnector.
- A module requiring an unsupported API level is rejected before initialization.

## Report

For every failure capture:

- `BUILD-COMMIT.txt`;
- CounterStrikeSharp version;
- CS2/DatHost server build;
- database version;
- command/action performed;
- server log excerpt;
- whether a reconnect, map change or hot reload happened immediately before it.

Do not work around a failure by disabling the affected feature. Record it against #157 (or its owning issue), reproduce it with the smallest reliable sequence, add an automated regression where possible, fix the integration/feature branch, and rerun the exact-head CI before the next disposable-server pass.
