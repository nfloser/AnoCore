# AnoTournament Workshop UI contract

This is the **resource and projection contract** for the tournament viewer
([#381](https://github.com/nfloser/AnoCore/issues/381)). The Panorama
presenter and shared event repository integration are now implemented in source,
but the native CS2 client and Workshop distribution remain unverified.

## Why fixed slots?

Valve Panorama resources are compiled and mounted by clients through the
`anomeme_ui` Workshop addon. Unlike a web interface, this server-facing
`custom_hud_layout` API currently supports updating known dialog variables
and CSS classes, but not arbitrary runtime creation of new panels. The
layout therefore bundles every dynamic slot upfront.

The UI has **five tabs**: Overview, Standings, Bracket, Matches, Teams.
Fixed bounds: ten table/team buttons, sixteen bracket cards, sixteen
fixture text rows, seven roster members (five starters and two substitutes)
and four overview summaries. The server fills the selected page,
clears/marks unused slots and moves between pages without adding XML.

- `ano_tournament_root` toggles the `shown` class and exactly one
  `view-<tab>` class.
- `ano_tournament_table_{0..9}` and `ano_tournament_team_{0..9}` are
  stable clickable rows; `ano_tournament_match_{0..15}` are clickable match cards.
- `ano_tournament_prev`, `ano_tournament_next` and
  `ano_tournament_page` provide bounded paging.
- Every ID-bearing label has `text="{s:text}"` and is filled through
  `ICustomHudService.SetText`. All displayed data must be treated as plain
  text; never inject untrusted HTML, styles or resource paths.
- Use CSS `hidden` for unused rows and only allow click handlers for
  the current player session and visible page/revision.
- The right details panel includes `ano_tournament_team_name`,
  `ano_tournament_team_status` and `ano_tournament_roster_0..6`.
- Config and match-result changes update the database and then re-render
  existing clients through the exact same IDs. There is no Workshop
  republish for event/roster/point/ranking/format data.

## Distribution and acceptance

`ui/AnoCore/build.ps1` includes `ano_tournament.xml` and
`ano_tournament.css` in the same `anomeme_ui` output as menu and veto.
Publish only after Workshop Tools compilation and disposable CS2 client acceptance. The native presenter and shared event database integration now exist in source.

CI performs offline XML/source contract tests but **does not compile Valve
resources or exercise a CS2 client**. Before final Workshop publication:
compile using CS2 Workshop Tools, test source-style compatibility, check
cursor close/reconnect/map-change cleanup, and visually smoke-test 4:3,
16:9 and ultrawide. A client must mount the published pack automatically
via server addon distribution. A compiled Workshop update is necessary
when the *structural XML or CSS* changes, not when tournament data changes.

## Dynamic runtime projection

When `PanoramaMenusEnabled` is enabled, `!anotournament` opens the
dedicated tournament HUD. AnoMenu offers that destination and closes its own
overlay before displaying it. Without Panorama enabled, the existing
text-only tournament status entry remains available.

The `TournamentPanoramaPresenter` loads the current event from shared
MySQL and projects dynamic team names, standings, fixtures, knockout
pairings and registered SteamIDs through `ICustomHudService.SetText`.
Panorama tabs, page buttons, roster selection and bracket match selection
run through session-validated click callbacks; close/unload releases input.

The server polls the shared event revision every three seconds **only
while the menu is open**. Unchanged snapshots are not rerendered; a delayed,
older revision cannot overwrite a newer one. Switching active event IDs
resets the client's selected page and team. The Workshop addon is not
recompiled when only database data changes.

The knockout display now projects persisted confirmed playoff winners, resolved
next-round pairings, ready/waiting states and champion through the same compiled
slots. Result-only changes never require Workshop republishing. With `tournament-servers.json` enabled, shared expiring heartbeats project
server identity, player session and actual AnoTournament readiness. Presence
changes refresh the roster independently of event revisions. Multiple reported
sessions are labeled explicitly instead of assuming readiness. Without that
configuration, presence remains labeled **local server only**. The existing single-active-match runtime has not yet been
replaced with server-scoped match leases. MatchZy/CS2 result ingestion,
Swiss and double elimination also require separate implementations.
Do not treat those items as complete.

## Production acceptance

- Compile this pack using CS2 Workshop Tools on Windows. CI does not have
  Valve's resource compiler or live client.
- Join a clean client with the Workshop addon mounted automatically.
- Verify `!anotournament`, menu transitions, close/cursor release,
  tabs, group paging, team and match click, reconnect and map change.
- Visually inspect 4:3, 16:9, stretched and ultrawide with 3, 5 and 10 teams.
- Run two connected AnoCore servers against the same MySQL and verify that
  a confirmed fixture updates an already-open menu after a revision change.
- **Publish the first updated Workshop addon only after this acceptance,
  not once per code commit.** Once published, event data updates do not
  require another upload.
