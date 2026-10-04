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

`!anogamestats [map]` returns the invoking player's persisted counters globally or for one map. `!anostatsmenu [map] [weapon]` opens a paginated shared menu that combines the same persisted K/D/A, detail, hitgroup and gameplay repositories without creating a second statistics store. Map filtering applies to detail and gameplay counters; weapon filtering applies to detail/hitgroup rows. Existing `!anokda`, `!anodetailstats` and `!anohitgroups` remain direct command views over the same ledgers.

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
6. Open `!anostatsmenu`, navigate multiple pages and repeat with map/weapon filters; reconnect while a menu is open and confirm the old view cannot act on the new session.
7. Reconnect/restart and confirm `!anogamestats` plus map filtering remain stable.
8. Record host/plugin/MariaDB versions and observations under the disposable-server acceptance issue.

Native verification remains part of #157/#23 and is not implied by green CI.
