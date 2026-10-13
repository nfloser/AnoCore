# Tournament fixture → CS2 match planning

Tracking: #409 (part of #379 and #380).

`TournamentFixtureMatchPlanner.Plan(eventDefinition, groupResults, playoffResults)`
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
- Stage BestOf defaults are BO1 for groups and BO3 for playoffs unless
  the event explicitly overrides them.

## Safety boundary

**A planned fixture is not a live server reservation and does not mean
the players are online or ready.** The planner does not mutate the event
database, call `TournamentRecoveryService.BeginAsync`, issue a
map-change command or bypass the existing server-scoped match lease.

Before automatic assignment, a separate coordinator must:

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

Those server and native match adapter steps remain follow-up work.
This planner alone must not be described as hands-off tournament
automation.

## Verification

Parameterized MSTest for 3 through 10 group sizes, stable GUIDs,
unique five-person lineups, completed fixtures, Swiss round unlocking,
playoff gating and winner progression, direct Double Elimination and
invalid input. No Workshop addon update is needed for this backend work.
