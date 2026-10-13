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

## Per-server live matches

When server identity is enabled, native recovery and tournament commands use
migration 25's server-scoped active pointer. Distinct servers can own distinct
matches; a unique active-match key prevents two scoped servers from activating
the same match. Writes validate both active ownership and the live instance lease.
Default installations keep the legacy singleton behavior.

Do not reuse one `tournament-match.json` MatchId on two servers. Deactivate the
legacy active match before enabling scoped operation, then load an explicit
match configuration on each server. A legacy active match is not silently moved
into a scope. Shared event data stays global; live gameplay ownership is local.
A duplicate server ID is fenced before recovery. After a crashed instance,
wait thirty seconds and reload the plugin before recovering that server ID.

Server-scoped ownership is not yet an automatic event-fixture scheduler. Operator
match loading is still explicit; automatic group/playoff fixture assignment and
CS2 result capture remain separate integration steps.
