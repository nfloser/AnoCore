# Shared tournament server presence

Use the same database on participating servers, with a distinct explicit
`tournament-servers.json` identity per server:

```json
{"Enabled": true, "ServerId": "dathost-test-1"}
```

Presence snapshots replace the entire connected-player list every ten seconds.
The database clock grants a thirty-second lifetime, so disconnected/crashed
servers disappear even when shutdown cleanup cannot run. A running instance
cannot overwrite another live instance with the same configured server ID;
a restarted instance waits for the previous lifetime to expire.

A player snapshot contains SteamID, session ID, display name, current AnoTournament
match ID and actual ready membership. A connected player is not automatically
ready. MatchZy readiness has no adapter yet and is not inferred.

Migration 24 creates a separate bounded presence table. Expired rows are excluded
from reads; identities remain available for later renewal. This table does not
change the legacy singleton match controller or assign parallel matches.

Acceptance: use two differently named test servers against one database, verify
presence and ready/unready, reconnect a participant, stop one server and verify
that its players disappear within thirty seconds plus one UI polling interval.
Try duplicate server IDs and verify that the second publisher fails explicitly.
