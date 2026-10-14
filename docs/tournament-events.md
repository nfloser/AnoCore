# AnoTournament events: teams, standings and shared results

Tracking: [#378](https://github.com/nfloser/AnoCore/issues/378), part of [#21](https://github.com/nfloser/AnoCore/issues/21).

## Status and compatibility

Events support 3-10 registered teams, deterministic group schedules and standings,
shared MySQL revisions, explicit configuration import and audited result commands.
Confirmed playoff winners resolve later pairings. Optional
`PlayoffQualifiersPerGroup` derives finalists once every group fixture is complete.
See [playoff rules and commands](tournament-brackets.md).

[Server identity configuration](tournament-servers.md) enables expiring cross-server
player presence and fenced per-server active live matches. Default installations
retain the legacy single-match path. Event data is shared; gameplay ownership is
server-specific.

`TournamentMatchDefinition` still represents one CS2 encounter. Automatic event
fixture assignment and CS2/MatchZy result ingestion are not implemented. The
Panorama viewer is integrated in draft PR #387 and requires Workshop Tools and
real client acceptance before release. Swiss/double elimination remain open.

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

## Displaying offline players by name

A team may optionally include a `PlayerNames` object in `config/tournament-event.json`.
Its keys are the same SteamID64 values already listed in `Starters` or
`Substitutes`; it never replaces or registers those identities. For example,
the ANOMEME offline demonstration team can add:

```json
"PlayerNames": {
  "88000000000000001": "Testspieler 1",
  "88000000000000002": "Testspieler 2",
  "88000000000000003": "Testspieler 3",
  "88000000000000004": "Testspieler 4",
  "88000000000000005": "Testspieler 5"
}
```

Place this property inside the matching team's object, next to `Starters`
and `Substitutes`. Each label must be printable and 1–32 characters long.
References to players not in that team's roster are rejected. Absent labels
default to the current session/presence nickname when available, otherwise to
SteamID64 alone. A supplied label appears as `Name (SteamID64)` along with
the truthful offline/online and ready status. Synthetic SteamIDs do not become
online players or match participants merely by assigning a display name.

After installing an AnoCore build containing this feature, update the fixed
configuration file and use `css_anotournamenteventload` to reimport the
event. Editing the file alone does not change the event already stored in
MySQL. Previously confirmed results remain under their original fixture
identities; use a new event ID for an independent clean-room tournament test
rather than rewriting prior results. Existing events without `PlayerNames`
continue to work. No new database migration or Workshop XML/CSS is required
for this display-only change, provided the tournament viewer resources have
already been deployed.

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
css_anotournamenteventbracket
css_anotournamenteventplayoffresult <match-id> <winner-team-id> <winner-maps> <loser-maps>
css_anotournamenteventresult <fixture-id> <home-maps> <away-maps> <home-rounds> <away-rounds>
```

The values for `fixture-id` must come from the generated schedule (for
example `main:r1:m1:legends:raiders`); do not guess opponent ordering.
Result confirmation is one-way in this increment: corrections must be
implemented through a separately reviewed and audited operation, rather
than silently overwriting a confirmed result.

**Remaining integration work:** automatic event match assignment, automatic
CS2/MatchZy result capture, Swiss and double elimination, configurable group
crossing/reseeding and native Workshop/DatHost acceptance. Shared presence,
server-scoped ownership, playoff persistence and group qualification are already
implemented; the Panorama viewer remains a draft until native acceptance.
This is not yet production-ready tournament automation.

## Stage formats and scheduling

Events now select `GroupFormat` (`"RoundRobin"` or `"Swiss"`) and
`PlayoffFormat` (`"SingleElimination"` or `"DoubleElimination"`). Existing JSON
without these fields retains its original round-robin/single-elimination behavior.
`GroupStageEnabled` and `PlayoffsEnabled` both default to true; set one false for
an event consisting only of a group league or only of playoffs. Qualification
requires both stages. Disabling a stage rejects results for that stage.

`TournamentSchedules.Generate` is the authoritative group schedule. Swiss starts
with seeded upper-half versus lower-half pairings, then minimizes point differences
without repeating opponents. The next round becomes visible only when every real
fixture of the current round is confirmed. Odd groups award one bye worth
`WinPoints`, with no invented map result or game played; the lowest-ranked eligible
team receives the bye and no team receives a second bye while another has none.
`SwissRounds` defaults to ceil(log2(group size)); configured rounds may not exceed
ceil(group size / 2), the bounded rematch-free policy for this implementation.
`Buchholz` can be added to the existing tiebreaker list; it sums the current points
of played opponents. The standings expose bye counts separately.

Double elimination sends upper-bracket losers into a generated lower bracket.
A team is eliminated on its second loss. `GrandFinalReset` defaults to true:
if the lower-bracket finalist wins the grand final, a reset match becomes ready;
if the undefeated finalist wins, the reset stays disabled. Setting the policy false
makes the first grand final decisive. Results for an inactive reset are rejected.
All participant slots and progression derive from confirmed results, including
loser references; no UI or operator needs to edit the bracket after a result.

Optional `GroupBestOf` / `PlayoffBestOf` accept 1, 3 or 5 and require decisive
series scores of 1, 2 or 3 winning maps respectively. Omitting them preserves the
legacy manually entered result policy. After the first playoff result, format,
reset, BestOf and Swiss-round policies and qualified seeds cannot be changed.
These additions do not themselves start a CS2 match or ingest MatchZy events.

## Presets and preview

The fixed import document accepts `SchemaVersion: 1` (missing defaults to 1).
Unknown schema versions are rejected. `css_anotournamenteventpreview` validates
that file and prints team/group sizes, match-count bounds, qualification and byes
without writing or activating an event. Preview can run before `Enabled=true` if
the roster definition is valid.

`css_anotournamenteventpreset <RoundRobin|Groups|Swiss|SingleElimination|DoubleElimination>`
clones the real configured teams into a validated format and atomically stages it
in that same fixed file. It does not invent SteamIDs or publish a tournament.
Presets choose BO1 groups and BO3 playoffs. Groups uses two balanced groups when
there are at least four teams, otherwise one group, with two qualifiers per group.
Swiss uses Buchholz ahead of map difference and initial seed. Double elimination
includes the conditional reset, hence a one-match range in the preview.

After checking the preview, set `Enabled=true` and use
`css_anotournamenteventload` to publish under the shared revision guard. Preset
changes for an event that already has confirmed results are rejected; use a new
stable event ID for a different format. Both staging and publication require
`ano.tournament.manage` and are audited. Existing JSON and the independent
`tournament-match` workflow remain compatible; no arbitrary path is accepted.

`!anotournamentadmin` opens the authorized Ingame configuration menu and is linked
from `!anoadminmenu`. It uses the existing menu renderer, so it needs no additional
Workshop resource. A publication confirmation pins the configuration fingerprint
and stored event revision; changed files or concurrent writes require a fresh
preview. Confirmation is consumed before the command awaits, and reconnects or
permission revocation invalidate the operation. Console imports without these
optional pinned arguments retain their existing behavior.
