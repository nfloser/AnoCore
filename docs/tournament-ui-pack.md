# AnoTournament Workshop UI contract

This is the **static resource contract** for the eventual tournament viewer
([#381](https://github.com/nfloser/AnoCore/issues/381)). It is not yet a
live wired menu: rendering and database services are separate changes.

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
Do not publish until the native presenter and backend are integrated.

CI performs offline XML/source contract tests but **does not compile Valve
resources or exercise a CS2 client**. Before final Workshop publication:
compile using CS2 Workshop Tools, test source-style compatibility, check
cursor close/reconnect/map-change cleanup, and visually smoke-test 4:3,
16:9 and ultrawide. A client must mount the published pack automatically
via server addon distribution. A compiled Workshop update is necessary
when the *structural XML or CSS* changes, not when tournament data changes.

## Deferred integration

The follow-up integration must register the custom HUD, implement all tab
and pagination click handlers, project versioned shared-DB snapshots to
known slots, and expose navigation via AnoMenu. Until this exists,
no functioning in-game tournament viewer is claimed.
