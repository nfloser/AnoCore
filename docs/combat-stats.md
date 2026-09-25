# Combat statistics

Issue #78 extends the Stats module with persisted kills, deaths and assists. Migration 006 stores one row per player death and uses a deterministic event ID to make repeated delivery idempotent. Restarted repositories read the same totals. A conflicting payload under an existing ID raises an error instead of silently rewriting history.

The native `player_death` hook records deaths only for tracked, connected human victims. A valid opposing human attacker gains one kill; world deaths, suicides and teamkills add no kill. The victim gains one death. A distinct, tracked, connected human assister gains one assist on a valid opposing-team kill. Bots and spectators do not gain credit. `anokda` displays the invoking player's saved kill/death/assist totals.

The event ID uses the server process identity, current map, an approximate map start time, tick count and victim SteamID64. This makes duplicate callbacks in the same tick idempotent. The approximate map epoch and native event mapping need real-server verification, especially map transitions and plugin hot reload. The initial scope counts warmup and does not implement per-map/weapon counters, round policies, rankings, resets or statistics menus.

## Disposable-server acceptance

1. Deploy the exact PR artifact and apply migration 006 on a disposable MariaDB/CS2 server. Confirm startup and `anokda` for a new human player.
2. With two humans, record one opposing kill, one suicide, one world death and one teamkill. Confirm death and kill totals and reconnect persistence.
3. With three humans, verify a distinct opposing assister receives exactly one assist; bots and same-team participants receive no credit.
4. Repeat across map changes, hot reload and restart. Check no unexpected duplicate or missing rows; verify `anokda` and unload.
5. Record host version, plugin SHA, MariaDB version and observed results under #23 before merging the draft PR.
