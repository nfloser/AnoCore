# Combat rank (development)

The Ranks module reads durable kill, death and assist totals from the Stats combat ledger. It does not maintain a second counter store. A connected player can use `css_anorank` (chat: `!anorank`) to display the rank name, points and deterministic leaderboard placement and exact points remaining to the next configured threshold. Players without persisted combat events are shown as unranked. At the final threshold it reports that the highest configured rank was reached. Console and disconnected callers are rejected. `css_anotopranks [page]` is available to players and the server console and shows five persisted score entries per page, ordered by points descending and SteamID64 ascending on ties. Pages are limited to 1–1000. Connected players can use `css_anoranks [page]` (chat: `!anoranks`) to open the shared rank menu. It shows their current rank, adjusted points, placement, next-threshold progress and five deterministic leaderboard entries, with bounded previous/next navigation. The menu reuses the same queries and maintains no second score store.

## Administrative commands

- `!anogiverankpoints <target> <points> [reason]` — `ano.ranks.points.give`
- `!anotakerankpoints <target> <points> [reason]` — `ano.ranks.points.take`
- `!anosetrankpoints <target> <points> [reason]` — `ano.ranks.points.set`
- `!anoresetrankpoints <target> [reason]` — `ano.ranks.points.reset`

Targets may be one unambiguous online player or an explicit SteamID64 for an offline player. Player actors cannot target themselves or players with equal/higher immunity. Server console calls are allowed by the shared command policy. Give/take values must be positive; set accepts the configured adjustment range. Every successful operation is durably audited in the same transaction.

On startup `config/ranks.json` is created with the defaults below if missing:

```json
{
  "KillPoints": 2,
  "AssistPoints": 1,
  "DeathPenalty": 1,
  "NotifyRankChanges": true,
  "NotifyAdministrativeRankChanges": true,
  "Thresholds": [
    { "Name": "Recruit", "MinimumPoints": 0, "Tag": "[Recruit]" },
    { "Name": "Veteran", "MinimumPoints": 10, "Tag": "[Veteran]" },
    { "Name": "Elite", "MinimumPoints": 100, "Tag": "[Elite]" }
  ]
}
```

Points = max(0, kills × KillPoints + assists × AssistPoints − deaths × DeathPenalty). The highest threshold at or below the point total determines the rank. Kills award 1–1000 points; assists and death penalty allow 0–1000. There must be 1–100 strictly increasing thresholds starting at zero with printable names up to 48 characters. Each threshold can expose a printable tag up to 24 characters; an empty tag intentionally suppresses it. Invalid configuration disables the rank module and logs a composition error while shared services continue.

Changing the configuration takes effect after a plugin restart or reload; ranks are derived again from persisted combat totals. This means historical events are rescored under the new weights. A reusable transition policy detects promotions, demotions and unchanged ranks across exact or multi-rank threshold changes. A durable adjustment store records bounded per-player manual point adjustments with actor/time metadata and reset semantics. Rank view, progress, placement and top list add that adjustment to the combat-derived score in one database snapshot; the result is floored at zero. Adjustment-only offline SteamIDs participate in the top list. An atomic administration service performs concurrency-safe give/take/set/reset mutations and writes the matching administrative audit entry in the same transaction. The commands above expose this service through centralized permissions and target authorization. Durable combat writes now compare each affected player's adjusted score before and after the idempotent event. When `NotifyRankChanges` is enabled, connected players crossing a threshold receive one promotion or demotion chat message; replays and within-rank changes produce none. Local combat callbacks are serialized so overlapping events cannot skip an intermediate transition. When `NotifyAdministrativeRankChanges` is enabled, successful give/take/set/reset operations use their returned durable adjustments and one combat-total snapshot to notify an online target about a resulting threshold change. Notification preparation/delivery is best-effort after the atomic mutation and audit; failure is logged without reporting the committed command as failed. The module registers shared asynchronous placeholders `{rank.tag}`, `{rank.name}` and `{rank.points}`; consumers pass the player as a `PlayerId` under the case-insensitive `player` context key. Missing player context resolves to an empty value. This is the common tag data source, not a claim that native chat or clan tags are already rewritten; that adapter and native menu rendering still require live acceptance. The rank top list uses current weights and adjustments; it does not store a second derived score. Rank score arithmetic rejects overflow rather than silently wrapping.


## Player notification preference

When the rank module is composed with the shared player-toggle catalog it registers `rank.notifications` with a default of `on`. The server-level `NotifyRankChanges` and `NotifyAdministrativeRankChanges` configuration switches remain authoritative; the player preference can only suppress a message that the server would otherwise send. Both combat-driven and administrative promotion/demotion messages read the same persisted player setting before entering the CounterStrike notification adapter. Turning the option off does not skip combat persistence, administrative audit, score changes, rank evaluation or warmed chat/rank refreshes. A preference-read failure is logged and suppresses that presentation message instead of failing an already committed gameplay mutation.

Players can inspect the option with `anosettings`, change it with `anotoggle rank.notifications on|off|default`, or use `anosettingsmenu`. `default` removes the stored override and therefore returns to the enabled default. The toggle registration is owned by the rank module and disappears again if rank composition rolls back or the module unloads.

Disposable-server acceptance: verify a consuming chat/tag adapter renders configured tags without leaking them across reconnects, then verify first startup creates `ranks.json`, combat and administrative promotion/demotion chat delivery, server-level notification switches, per-player `rank.notifications` opt-out/default behavior, reconnect suppression for stale sessions, `!anorank` for two human accounts, `!anotopranks 1` ordering and pagination, exact threshold transitions through kills and assists, suicide/world death penalties, reconnect, reload with changed weights, and invalid configuration isolation. Record installed SHA and observations under #23. Native combat ingestion in #79 must pass its own acceptance gate before this rank can be released.
