# Combat rank (development)

The Ranks module reads durable kill, death and assist totals from the Stats combat ledger. It does not maintain a second counter store. A connected player can use `css_anorank` (chat: `!anorank`) to display the rank name, points and deterministic leaderboard placement and exact points remaining to the next configured threshold. Players without persisted combat events are shown as unranked. At the final threshold it reports that the highest configured rank was reached. Console and disconnected callers are rejected. `css_anotopranks [page]` is available to players and the server console and shows five persisted score entries per page, ordered by points descending and SteamID64 ascending on ties. Pages are limited to 1–1000.

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
  "Thresholds": [
    { "Name": "Recruit", "MinimumPoints": 0 },
    { "Name": "Veteran", "MinimumPoints": 10 },
    { "Name": "Elite", "MinimumPoints": 100 }
  ]
}
```

Points = max(0, kills × KillPoints + assists × AssistPoints − deaths × DeathPenalty). The highest threshold at or below the point total determines the rank. Kills award 1–1000 points; assists and death penalty allow 0–1000. There must be 1–100 strictly increasing thresholds starting at zero with printable names up to 48 characters. Invalid configuration disables the rank module and logs a composition error while shared services continue.

Changing the configuration takes effect after a plugin restart or reload; ranks are derived again from persisted combat totals. This means historical events are rescored under the new weights. A reusable transition policy detects promotions, demotions and unchanged ranks across exact or multi-rank threshold changes. A durable adjustment store records bounded per-player manual point adjustments with actor/time metadata and reset semantics. Rank view, progress, placement and top list add that adjustment to the combat-derived score in one database snapshot; the result is floored at zero. Adjustment-only offline SteamIDs participate in the top list. An atomic administration service performs concurrency-safe give/take/set/reset mutations and writes the matching administrative audit entry in the same transaction. The commands above expose this service through centralized permissions and target authorization. This subset does not yet deliver transitions as player notifications and does not implement rank tags or menus. The rank top list uses current weights and adjustments; it does not store a second derived score. Rank score arithmetic rejects overflow rather than silently wrapping.

Disposable-server acceptance: verify first startup creates `ranks.json`, `!anorank` for two human accounts, `!anotopranks 1` ordering and pagination, exact threshold transitions through kills and assists, suicide/world death penalties, reconnect, reload with changed weights, and invalid configuration isolation. Record installed SHA and observations under #23. Native combat ingestion in #79 must pass its own acceptance gate before this rank can be released.
