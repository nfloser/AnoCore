# Tournament fixture → CS2 match planning

Tracking: #409 (part of #379 and #380).

`TournamentFixtureMatchPlanner.Plan(eventDefinition, groupResults, playoffResults, defaultGroupBestOf, defaultPlayoffBestOf)`
returns **potential**, unclaimed 5v5 matches. It is independent of
CounterStrikeSharp, MySQL access and Valve Workshop assets.

- Group fixtures come from the authoritative `TournamentSchedules` generator.
  This means round robin and incremental rematch-free Swiss fixtures share
  the same identities that results and the Panorama standings use.
- All completed group fixtures are excluded; unresolved future Swiss
  rounds are **not** guessed before the previous round completes.
- Playoff matches are emitted only when both competitors are resolved and
  the result is not yet recorded. Even if the event's playoff field is
  seeded directly, an enabled group stage must finish before its matches
  become assignable.
- Single- and double-elimination matches, including grand-final reset
  when necessary, are handled by the same projection.
- Every proposed match has a deterministic GUID derived from the
  validated event ID, stage ID and fixture ID. Renaming a team does not
  regenerate its match ID. Changing fixture pairing generates a new ID.
- Each proposal contains the current team display names and tags,
  five registered starters, captain, knife/overtime defaults, and
  configured BO1/BO3/BO5. Substitutes are never silently inserted into
  the starting roster.
- The default stage BestOf is BO1 for groups and BO3 for playoffs when no
  alternative defaults are supplied; an explicit event-stage BestOf always wins.

## Read-only operator preview

The registered `css_anotournamenteventnext [page]` command uses this planner
to list up to six assignable pairings at a time. Each row includes the
stable source fixture ID, group/playoff stage, names, BO format and the
deterministic CS2 match GUID. Pages are 1-based; the command does **not**
reserve a server, create live gameplay, bypass permissions or mutate results.
The same event may be previewed from any server sharing the database.

## Consistency with automatic MatchZy assignment

The MatchZy claim planner and the read-only fixture preview now both call
the same canonical planner. They therefore agree on the source fixture ID,
participant sides, unplayed stage eligibility and the effective BO1/BO3/BO5.
If `tournament-matchzy.json` specifies `DefaultGroupBestOf` or
`DefaultPlayoffBestOf`, the same defaults are supplied to the operator
preview. Explicit `GroupBestOf` and `PlayoffBestOf` in the event take
precedence, without mutating the stored event definition.

The deterministic GUID shown by `anotournamenteventnext` is the independent
AnoCore standalone match identity. MatchZy uses a separate **numeric**
assignment ID for `matchid`. The common referent is the stable tournament
fixture ID, not an interchangeable `matchid` format.

MatchZy already supports transactionally claimed fixtures, server instance
fencing and auditable, idempotent result ingestion; see
[tournament-matchzy.md](tournament-matchzy.md). The standalone planner itself
remains read-only and never claims or starts a CS2 match.

## Safety boundary

**A planned fixture is not a live server reservation and does not mean
the players are online or ready.** The planner does not mutate the event
database, call `TournamentRecoveryService.BeginAsync`, issue a
map-change command or bypass the existing server-scoped match lease.

The automatic MatchZy coordinator must:

1. Check the current event revision and fixture identity again;
2. transactionally claim the fixture against the specific live server,
   with a unique active fixture owner and an expiring lease;
3. verify that the server has no active match and the registered
   rosters are not already competing elsewhere;
4. begin/recover the standalone match under the existing scoped runtime
   and persist the linkage;
5. on CS2/MatchZy completion, validate the actual match ID and source
   event fixture before recording result in MySQL; use idempotency keys;
6. release or recover the assignment safely after disconnect or crash.

The current MatchZy coordinator implements transactional claims and a
validated callback adapter. Native, two-server and Workshop client acceptance
is still required; this planner alone must not be described as hands-off
tournament automation.

## Verification

Parameterized MSTest for 3 through 10 group sizes, stable GUIDs,
unique five-person lineups, completed fixtures, Swiss round unlocking,
playoff gating and winner progression, direct Double Elimination and
invalid input. No Workshop addon update is needed for this backend work.
