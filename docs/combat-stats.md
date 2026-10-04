# Combat statistics

Issue #78 extends the Stats module with persisted kills, deaths and assists. Migration 006 stores one row per player death and uses a deterministic event ID to make repeated delivery idempotent. Restarted repositories read the same totals. A conflicting payload under an existing ID raises an error instead of silently rewriting history.

The `anotopkills [page]`, `anotopdeaths [page]` and `anotopassists [page]` commands list five ranked players per page, ordered by the selected count descending and SteamID64 ascending on ties. Deaths include suicides, world deaths and teamkills; assists exclude invalid and teamkill events. Pages are bounded to 1–1000; names come from saved profiles with SteamID64 fallback. Teamkills do not contribute. This reads persisted events only, so ranking reflects completed writes.

The native `player_death` hook records deaths only for tracked, connected human victims. A valid opposing human attacker gains one kill; world deaths, suicides and teamkills add no kill. The victim gains one death. A distinct, tracked, connected human assister gains one assist on a valid opposing-team kill. Bots and spectators do not gain credit. `anokda` displays the invoking player's saved kill/death/assist totals.

The death event ID uses the server process identity, current map, an approximate map start time, tick count and victim SteamID64. This makes duplicate callbacks in the same tick idempotent. The approximate map epoch and native event mapping need real-server verification, especially map transitions and plugin hot reload.

## Weapon fire, hits and damage

Issue #182 adds an optional `ICombatDetailRepository` without changing existing `ICombatRepository` consumers. Migration 009 persists raw `weapon_fire` and `player_hurt` events with deterministic identities, map and weapon keys, hitgroup, health/armor damage and team/self context. Exact replays are idempotent; an existing ID with a different payload fails rather than rewriting history.

`anodetailstats [map] [weapon]` shows the invoking player's persisted shots, hits, health/armor damage and head-hit count. `anohitgroups [map] [weapon]` returns a bounded hitgroup breakdown. By default offensive hit/damage queries exclude team damage and self damage; weapon-fire counts remain raw shots. SDK consumers can explicitly include those damage categories through `CombatDetailFilter`. World/unattributed damage remains distinguishable because it is stored with no attacker and therefore is not credited as player offense.

Native handlers accept tracked connected human sessions only and queue persistence through the existing asynchronous observer path rather than waiting for MariaDB on the CS2 callback thread. Bots/HLTV are not credited. Map/weapon keys are bounded and control characters are removed before contract validation.

This still does not implement grenade-specific counters, objectives, MVPs, round/match aggregation, warmup/min-player/FFA policy, reset, or a full statistics menu. Those remain separate #18 work.

## Disposable-server acceptance

1. Deploy the exact PR artifact and apply migration 006 on a disposable MariaDB/CS2 server. Confirm startup and `anokda` for a new human player.
2. With two humans, record one opposing kill, one suicide, one world death and one teamkill. Confirm death and kill totals and reconnect persistence.
3. With three humans, verify a distinct opposing assister receives exactly one assist; bots and same-team participants receive no credit.
4. Fire known AK-47/AWP shots and land controlled body/head hits. Verify `anodetailstats` and `anohitgroups`, then filter by map and weapon.
5. Exercise self damage, team damage and world/fall-style damage. Confirm default player-offense totals exclude self/team damage while the raw rows remain distinguishable.
6. Repeat across map changes, hot reload and restart. Check no unexpected duplicate or missing rows; verify `anokda`, detail queries and unload.
7. Record host version, plugin SHA, MariaDB version and observed results under #23 before merging the draft PR.
