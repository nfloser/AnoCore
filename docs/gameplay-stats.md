# Gameplay statistics

AnoCore stores non-K/D/A gameplay counters in an idempotent event ledger (`ano_gameplay_stats`, migration 010). The ledger uses a fixed `GameplayStatKind` enum rather than arbitrary statistic keys. Each native event receives a deterministic event id, so exact callback replays do not double-count and conflicting payload reuse fails.

## Recorded counters

The current native adapter records:

- grenade throws;
- bomb plants and defuses;
- hostage rescues and kills;
- round MVPs;
- round participation, T/CT participation and round win/loss;
- match win/loss in team mode and FFA mode;
- first blood, headshot kills, noscope kills, penetration kills, through-smoke kills, attacker-blind/flashed kills, domination, revenge and flash assists.

`!anogamestats [map]` returns the invoking player's persisted counters globally or for one map. `!anopersonalstatsmenu` opens the compact own view; `!anostatsmenu` selects server-wide leaderboards, including offline players. The existing filtered view is now `!anostatdetails [map] [weapon]`, combining persisted K/D/A, detail, hitgroup and gameplay repositories. Map filtering applies to detail and gameplay counters; weapon filtering applies to detail/hitgroup rows. Existing `!anokda`, `!anodetailstats` and `!anohitgroups` remain direct views over the same ledgers. See [statistics-menus.md](statistics-menus.md) for categories, minimum samples, native headshot context and pagination.

## Eligibility policy

`config/gameplay-stats.json` is created through the shared versioned config store. Defaults mirror the reference behavior:

- `WarmupStats`: `false`
- `MinimumPlayers`: `4`
- `FreeForAll`: `false`

When the gameplay-statistics module is active, the same warmup/minimum-player gate is applied before K/D/A, weapon-fire and player-hurt ingestion. FFA mode disables same-team suppression for combat credit and determines match winner by highest player score with a stable SteamID tie-break.

Bots and HLTV clients are not represented by the tracked human-player registry and therefore do not receive gameplay-stat credit.

## Native acceptance

Automated tests cover contract validation, deterministic round/match outcomes, eligibility behavior, replay conflict detection, map filtering, restart persistence and runtime composition. A disposable CS2 server still must verify the native event/property mapping and timing:

1. Confirm no stats change during warmup with defaults and below four tracked humans.
2. Reach the player threshold and verify grenade/objective/MVP counters.
3. Complete rounds on both teams and verify played/team/win/loss counters.
4. Trigger the supported special kill flags and a flash assist.
5. Complete a team match and an FFA match and verify winner/loser counters.
6. Open `!anopersonalstatsmenu` and server-wide `!anostatsmenu`; navigate rankings including offline players. Repeat the detail view with `!anostatdetails [map] [weapon]`; reconnect/close during a read and confirm the old view cannot act on the new session.
7. Reconnect/restart and confirm `!anogamestats` plus map filtering remain stable.
8. Record host/plugin/MariaDB versions and observations under the disposable-server acceptance issue.

Native verification remains part of #157/#23 and is not implied by green CI.


## Administrative reset

`!anoresetstats <target> [reason]` requires `ano.stats.reset` and uses the shared target/immunity policy. Online players can be selected by the normal explicit target syntax; offline targets require SteamID64.

A reset is non-destructive. AnoCore stores a per-player cutoff and all combat, detail, hitgroup and gameplay aggregates ignore that player's events at or before the cutoff. The immutable event ledgers remain intact, so resetting one participant never removes another participant's kill, death, assist or other history. Rank adjustment records are intentionally independent and are not deleted by a statistics reset.

The cutoff and `statistics.reset` administrative audit entry are committed in one transaction. A later reset must advance the cutoff. Disposable-server acceptance should verify an online and offline reset, permission/immunity denial, immediate menu/command output after reset, post-reset event accumulation and restart persistence.
