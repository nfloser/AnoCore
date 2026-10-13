# AnoTournament event model: 3–10 team groundwork

Tracking: [#378](https://github.com/nfloser/AnoCore/issues/378), part of [#21](https://github.com/nfloser/AnoCore/issues/21).

## Status and compatibility

This first increment adds **pure, engine-independent tournament-event contracts** alongside
the existing two-team `TournamentMatchDefinition`. It does **not** activate multi-team
events on the server, load the example JSON automatically, change the existing
`tournament-match` file or deliver a Panorama tournament HUD. Event persistence,
server-scoped match ownership, live presence, bracket advancement, Swiss, operator
commands and Workshop UI are separately tracked under #379–#382.

The separation is intentional: `TournamentMatchDefinition` and existing MySQL
match recovery still represent **one CS2 encounter**; a future event scheduler
will convert scheduled event fixtures into those encounters.

## Event and team contracts

`TournamentEventDefinition` defines a stable event ID, display name, five-player
lineups and between **3 and 10 registered teams**. Teams have stable, lowercase
IDs independent of their editable display name/tag, one captain among the five
starters, and up to two substitutes by default. Every SteamID is unique across
the whole event including substitute lists. An optional initial seed has a
unique value within the total team count.

Groups contain at least two teams each and must form a complete partition of
the event; an empty `Groups` list represents one implicit group named `main`.
Group IDs are stable and independent of the display label.

For example, the following partial structure describes group assignments; a
complete event also requires five distinct SteamID64 values for each team:

```json
{
  "Id": "autumn-cup-2026",
  "Name": "AnoMeme Autumn Cup",
  "TeamSize": 5,
  "MaxSubstitutes": 2,
  "Teams": [
    { "Id": "legends", "Name": "AnoMeme Legends", "Tag": "LEG",
      "CaptainSteamId": 111, "Starters": [111, 112, 113, 114, 115],
      "Substitutes": [], "InitialSeed": 1 },
    { "Id": "raiders", "Name": "Night Raiders", "Tag": "NR",
      "CaptainSteamId": 121, "Starters": [121, 122, 123, 124, 125],
      "Substitutes": [], "InitialSeed": 2 },
    { "Id": "hope", "Name": "Last Hope", "Tag": "LH",
      "CaptainSteamId": 131, "Starters": [131, 132, 133, 134, 135],
      "Substitutes": [], "InitialSeed": 3 }
  ],
  "Groups": [
    { "Id": "main", "Name": "Liga", "TeamIds": ["legends", "raiders", "hope"] }
  ],
  "Standings": {
    "WinPoints": 3,
    "DrawPoints": 1,
    "LossPoints": 0,
    "AllowDraws": false,
    "Tiebreakers": ["MapDifference", "RoundDifference", "InitialSeed"]
  }
}
```

**This is a schema example, not a deployable config in the current plugin.**
The short numeric SteamIDs are placeholders, not real SteamID64 assignments.
Production import should validate actual SteamID64 formats and must restrict
file locations to a predefined server config root.

## Group pairings and standings

`TournamentRoundRobin.Generate(definition, groupId, legs)` uses the circle
method with deterministic round/fixture IDs. It supports one or two legs. Odd
group sizes receive a rotating bye; each team meets every other team exactly
once per leg. A 3-team league schedules three matches, a 5-team league ten,
and a 10-team league 45.

`TournamentStandings.Calculate` consumes confirmed results referencing
generated fixture IDs, never mutable display names. It rejects invalid map
scores, unknown fixtures and repeated result IDs. Results are not persisted
or accepted from live CS2 in this first increment; the future integration
must confirm provenance and series completion.

The result-derived table includes played, wins/draws/losses, points, maps
for/against, rounds for/against, the two differentials and points behind the
group leader. Sort order is **points first**, then configured and explicit
tiebreakers (map difference, round difference, maps/rounds won, initial seed),
then stable team ID. The current stage deliberately does not implement
head-to-head mini-leagues or Buchholz; those require their own tests and
competition-specific policies before being offered as presets.

## Server and UI follow-ups

The authoritative event state will be stored in shared MySQL, with revisioned,
audited writes. Each game server must own its own match lease rather than
reusing the legacy global `ano_tournament_runtime` singleton. Other servers
read consistent event snapshots and receive revision updates, with polling
as a fallback. Online and ready are distinct per-SteamID states; the server
owns availability rather than trusting clients.

AnoMenu will launch a dedicated fullscreen-style Panorama view with tabs
**Overview / Table / Bracket / Matches / Teams**. Team display names will be
clickable in the table and bracket to show registered starters, captain,
substitutes, online/ready status and current server. Public read access
and restricted administrative actions must remain separate. Workshop resources
remain static; server data updates must not require republishing the addon.

See the follow-up issues for actual server integration and acceptance gates.

## Verification

- MSTest includes group scheduling for each team count 3 through 10, double
  legs, byes, multi-group isolation, roster validation, standings tie-breaks,
  points-behind-leader and invalid/duplicate result rejection.
- Existing `TournamentMatchDefinition`, `TournamentMatchRuntime` and
  AnoVeto paths remain unchanged.
- Real multiplayer sync, configured event import, UI interaction and
  tournament operations require additional integration/test-server validation.

## Authoritative shared event storage (migration 022)

The new `MySqlTournamentEventRepository` stores a validated event definition
and confirmed fixture results in `ano_tournament_events` and keeps an
independent active-event pointer in `ano_tournament_event_runtime`. This is
**separate** from the existing single-active-live-match pointer: no existing
AnoMatch recovery table is changed.

All servers configured with the same MySQL connection can load the active
event and read its standings from the same revision. An event save performs
one conditional SQL update `WHERE revision = expected` inside a transaction;
a stale write throws `TournamentEventConcurrencyException` and never
overwrites a newer result. Every event read revalidates its JSON state.
A new result can only refer to a deterministic scheduled fixture ID.

Event admins use the fixed `config/tournament-event.json` import document.
Start by copying `examples/server-profiles/anomeme/tournament-event.json`,
replacing all synthetic SteamIDs, editing names and groups, and setting
`Enabled: true`. The file is only imported on explicit command; the database
is authoritative thereafter. No arbitrary filesystem paths are accepted.

Operator commands (console, or player with `ano.tournament.manage`):

```text
css_anotournamenteventload
css_anotournamenteventstatus
css_anotournamenteventstandings main
css_anotournamenteventresult <fixture-id> <home-maps> <away-maps> <home-rounds> <away-rounds>
```

The values for `fixture-id` must come from the generated schedule (for
example `main:r1:m1:legends:raiders`); do not guess opponent ordering.
Result confirmation is one-way in this increment: corrections must be
implemented through a separately reviewed and audited operation, rather
than silently overwriting a confirmed result.

**Not yet provided by this persistence layer:** automatically capturing
CounterStrikeSharp/MatchZy wins, server-scoped concurrent live-match leases,
shared player presence/ready heartbeat, GUI controls and live Panorama
projection. Those remain tracked under #379–#382. Until the complete UI
and game adapters are validated on DatHost, this is not production-ready
tournament automation.
