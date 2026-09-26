# Combat rank (development)

The Ranks module reads durable kill, death and assist totals from the Stats combat ledger. It does not maintain a second counter store. A connected player can use `css_anorank` (chat: `!anorank`) to display the rank name, points and deterministic leaderboard placement and exact points remaining to the next configured threshold. Players without persisted combat events are shown as unranked. At the final threshold it reports that the highest configured rank was reached. Console and disconnected callers are rejected. `css_anotopranks [page]` is available to players and the server console and shows five persisted score entries per page, ordered by points descending and SteamID64 ascending on ties. Pages are limited to 1–1000.

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

Changing the configuration takes effect after a plugin restart or reload; ranks are derived again from persisted combat totals. This means historical events are rescored under the new weights. This subset does not implement administrative rank overrides, notifications, rank tags or menus. The rank top list is calculated from the same persisted ledger and current weights; it does not store a second score. Rank score arithmetic rejects overflow rather than silently wrapping.

Disposable-server acceptance: verify first startup creates `ranks.json`, `!anorank` for two human accounts, `!anotopranks 1` ordering and pagination, exact threshold transitions through kills and assists, suicide/world death penalties, reconnect, reload with changed weights, and invalid configuration isolation. Record installed SHA and observations under #23. Native combat ingestion in #79 must pass its own acceptance gate before this rank can be released.
