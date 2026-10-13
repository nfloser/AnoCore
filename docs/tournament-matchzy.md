# Automatic tournament matches with MatchZy

This integration targets the verified MatchZy **0.8.15** file/event contract
(upstream tag commit `ef289d512766b89b0f7bf3088208ae88235e61fa`). It does not
infer results from team names or CT/T side. MatchZy owns roster enforcement,
ready-up, knife/side selection, match cvars and series play on these servers.
AnoCore's legacy live-match enforcement/controllers are disabled when this mode
is enabled. Use dedicated tournament servers; do not load an unrelated MatchZy
match while automatic assignment is enabled.

## Configuration

1. Install MatchZy 0.8.15 and configure its ordinary server settings/maps.
2. Give each CS2 server a unique identity in `config/tournament-servers.json`:
   `{"Enabled":true,"ServerId":"match-01"}`. All instances use the same MySQL DB.
3. Set `config/tournament-matchzy.json`, for example:

```json
{
  "Enabled": true,
  "ResultBaseUrl": "https://tournament.example.com",
  "DefaultGroupBestOf": 1,
  "DefaultPlayoffBestOf": 3,
  "Maps": ["de_mirage", "de_inferno", "de_nuke", "de_ancient", "de_anubis"]
}
```

4. Run `AnoCore.Management.Host` with `TournamentMatchZy__Enabled=true` and
   `ANOCORE_MYSQL` pointing to that same DB. Terminate HTTPS at the deployment
   boundary. HTTP is accepted in generated configs only for a loopback URL.
   This optional ingress is disabled by default and does not change operator
   management authentication. It accepts POST `/tournament/matchzy/<numeric-id>`.
5. Import/activate the event with `css_anotournamenteventload`. Validate the map
   names are actually installed. Current assignment uses the first BestOf maps
   from this fixed, validated list and `skip_veto=true`; it does not yet run the
   interactive AnoVeto selection for every generated fixture.

Assignment selection and `css_anotournamenteventnext` now share the
canonical group/playoff fixture planner, including Swiss round gating and
explicit or default BestOf. The preview prints a deterministic standalone
AnoCore match GUID for reference; MatchZy loads a **numeric** assignment ID.
Their stable join key is the event fixture ID, not the match ID format.

When both teams' five starters are present on one server, the 10-second poll
claims a ready fixture. A SteamID visible on two live servers prevents the claim.
Claiming does **not** mark players ready: MatchZy requires five ready players
**per team**. The shared event row serializes claims; a fixture, team or server
already occupied by another assignment cannot be reused. Groups finish before
playoffs are assigned; Swiss waits for the previous round's results. Independent
servers can play disjoint fixtures concurrently. Players must join the intended
server themselves; this feature does not issue client connection commands.

Each claim stores the exact definition, team IDs, maps, numeric external ID and
server instance before sending the fixed console command. Generated MatchZy
config is atomically written to `csgo/cfg/anocore/tournament-matchzy.json` with
owner-only permissions on Unix. It contains a fresh 256-bit assignment bearer
secret; only its SHA-256 hash is stored in MySQL. Do not publish this generated
file. No arbitrary console input or user-controlled file path is passed to CS2.

## Result and failure behavior

`map_result` uses zero-based `map_number`, team1/team2 scores and stable winner
team. `series_end` must match the configured BO1/BO3/BO5 winning map count.
Either order is accepted. A series is confirmed only after all declared map
results are present and agree. Duplicates are no-ops; contradictory replacements
are rejected. The final event result, revision increment, assignment completion
and transition audit commit in one transaction. Restarting the HTTP host does
not lose accepted maps or create a second result.

Other MatchZy telemetry is acknowledged without altering tournament results.
Canceled or drawn series are not converted into a fake win. Open assignments
block event imports/manual result writes and switching to another event. The
server has a 30-second cooldown after completion/cancellation for MatchZy's
post-series reset. Read `css_anotournamentassignments` for pending/dispatched
status; dispatched means the console load was requested, not a proven live match.

A dispatch marker is committed before the native load command. A crash in that
window, missing MatchZy, rejected load or failed file write leaves the assignment
open for operator recovery. It is never silently loaded again or transferred to
another server. After acquiring the current server lease, an authorized operator
can use `css_anotournamentassignmentcancel <id>` to cancel that server's assignment
and request the fixed `css_endmatch` command. Delayed result callbacks then fail
and the fixture becomes available again. This action intentionally discards that
attempt; cancel only when the match must be abandoned.

MatchZy 0.8.15 does not durably retry HTTP failures. If ingress is unavailable,
recover the original `map_result`/`series_end` JSON from MatchZy's
`[SendEventAsync] SENDING DATA` logs into the fixed AnoCore config:

```json
{
  "AssignmentId": 42,
  "Payloads": ["{\"event\":\"map_result\", ...}", "{\"event\":\"series_end\", ...}"]
}
```

Replace the illustrative ellipses with the **complete original event JSON** and
save as `config/tournament-matchzy-replay.json`. Then run
`css_anotournamentmatchzyreplay` with `ano.tournament.manage`. At most six bounded
payloads are allowed. Repeated accepted events are safe; a partial failure reports
how many were accepted. Both replay and cancellation have operator audit entries;
automatic transitions have a separate durable assignment audit.

## Required native acceptance

Before enabling production: run two servers against one DB; connect 20 clients
for two disjoint matches; confirm five ready players per team, correct roster
placement and halftime sides, map changes, BO3 clinch, automatic results and next
pairings. Test duplicate/out-of-order events, host downtime and replay, process
restart during dispatch/live play, canceled series, duplicate ServerId rejection,
and the Panorama views on multiple Workshop clients. Verify the configured map
list and MatchZy reset delay on the target server. CI exercises contracts and
MySQL concurrency; it does not prove this native acceptance.
