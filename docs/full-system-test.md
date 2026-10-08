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
- With default gameplay-stat policy, confirm warmup and fewer than four tracked humans do not advance combat/gameplay counters. Then reach the threshold and verify grenade, objective, MVP, round and match counters with `anogamestats`.
- Open `anopersonalstatsmenu` for own totals and `anostatsmenu` for server-wide categories/rankings, including offline players. Check sample eligibility, deterministic placement, requester marking, bounded previous/next and clean titles. Open `anostatdetails [map] [weapon]` for retained detail/hitgroup filters; reconnect/close during a read and confirm obsolete results cannot reopen or replace another menu.
- With an authorized admin, run `anoresetstats` against one player and confirm only that player's historical combat/detail/gameplay aggregates reset; the other participants' statistics remain unchanged. Verify offline SteamID64 reset, immunity denial, new post-reset events and restart persistence.
- Switch the gameplay-stat config to FFA for a disposable-server pass: same-team combat must receive normal credit and the highest stable player score must receive the match win.
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

## Tournament team enforcement

Prepare one persisted active tournament match with two known human SteamIDs and a
deterministic T/CT assignment before this pass. The current package restores that
active state on plugin startup; match-creation commands are a later #21 package.

- Start/reload the plugin with a rostered player already connected on the wrong side; the player is moved to the persisted assigned side.
- Connect and reconnect each rostered player from the wrong side and verify the replacement session is corrected once.
- Attempt a normal team switch/join-team change for a rostered player. The tracked team change must be corrected back to the tournament side without an event loop.
- A player already on the assigned side must not be switched again.
- A non-rostered player must not be moved by tournament enforcement.
- Trigger reconnect while a correction is queued; no delayed action may affect the replacement `PlayerSessionId`.
- Hot reload with the match active and verify the persisted assignment is restored and existing rostered players are reconciled.
- Unload during/just before a correction and verify no later team mutation is executed.

Record whether CS2 visibly exposes a short wrong-team window before the corrective
`SwitchTeam`. If it does, add pre-command interception rather than treating the
post-event correction as sufficient for release acceptance.

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

## Independent progression and live configuration

Use a disposable configuration with short known XP thresholds, a current season,
a dated season challenge and ordinary kill/assist/utility challenge predicates.
Record lifetime XP, season XP and rank points before and after each action.

- `anoprogression` exposes only enabled sources; back/refresh/pages work and reconnect/unload closes old menus. Reconnect during a slow detail read must not show the old session's response.
- Controlled eligible kills, assists and objectives award lifetime XP once. `anoxp` shows next-level XP and the strongest configured UTC boost. Global/window/weekend boundaries and overlap match configuration; challenge/achievement payouts remain unboosted by default.
- `anotoggle progression.level-notifications off` suppresses level notices without changing XP. Re-enable it, cross one or several thresholds and receive one committed transition notice. Reconnect/restart/checkpoint replay must not repeat the reward or notice.
- Give/take/set/reset lifetime XP through the four authorized administrative commands with required reasons. Verify audit rows, nonnegative bounds, offline targeting and immunity/permission denial. Season XP, earned grant history and rank points remain unchanged by administration.
- Ordinary kill/assist challenges exclude self/teamkill/invalid-assist input. Utility challenges count enemy HE/fire health damage, excluding gun/armor/team/self damage. Resetting statistics preserves raw window-scoped challenge progress.
- Challenge prerequisites show locked state until committed parent completion. Completion rewards once across repeated checkpoints/restart; daily/weekly UTC rollover starts a fresh occurrence.
- Intermediate notices default off. Enable `progression.challenge-progress-notifications`, establish a silent baseline, then observe a positive incomplete-task increase. Repeated/decreased counts, enabling after prior progress and reconnect/reload must not catch up old notices. Completion uses its independent completion toggle.
- Permanent achievement tiers unlock once with prerequisites and persist across season changes. Ending/closing a season preserves lifetime progression and immutable historical results; start the next configured season and verify deterministic current/history leaderboards.
- `anoconfigs` lists `gameplay-xp` while the module is active. Reload valid weights/weekend multiplier through `anoreloadconfig gameplay-xp`; verify the next checkpoint/status uses them and existing grants retain their recorded values.
- Invalid weights/JSON and activation/checkpoint-interval/earn-start changes reject reload and preserve active policy. Hot reload/restart installs restart-only changes without duplicated commands/timers/registrations.

These native observations belong to the current owning issues (#229 and #303),
with rank/client behavior under #290. They do not follow automatically from green
MariaDB/contract tests; preserve the exact installed artifact and server versions.

## SDK host notifications

With a test subscriber owned by a module lifetime:

- Connected public/team accepted chat captures the correct sender/session, raw bounded input, channel and UTC time. Commands, empty input, gagged senders and failed formatting emit no accepted-chat fact; native pass-through still emits accepted connected chat.
- A throwing/asynchronously failing observer must not change chat routing or produce duplicates. Async callbacks revalidate session/lifetime and use proper native thread dispatch.
- Unload/hot reload starts exactly one advisory core-unloading fact with the correct reason; observer failure must not prevent normal teardown. Required cleanup remains `ShutdownAsync`/owned resources, since native unload does not await async observer work.
- Baseline API-level 1 modules and event-aware level 2 modules initialize; future API levels are rejected before initialization.

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
