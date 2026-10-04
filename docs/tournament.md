# Tournament match orchestration

Issue #186 introduces the engine-independent foundation for AnoTournament.

## Roster model

A tournament match owns two immutable rosters. Each roster has a bounded name and tag,
one captain and 1-10 unique SteamID-backed members. A player cannot belong to both
teams. The match configuration supports BO1, BO3 and BO5 and derives the required map
wins from the series length.

## State machine

The lifecycle is explicit:

`Setup -> Ready -> Veto -> Knife -> SideChoice -> Live -> Completed`

When knife rounds are disabled, veto moves directly to `Live`. A live or overtime
map can move temporarily to `Paused` and resumes to the exact prior state.
`Live -> Overtime` is allowed only when overtime is enabled.

Invalid or out-of-order transitions throw instead of silently repairing state. Series
completion is deterministic and stops as soon as either team reaches the configured
maps-to-win count.

## Reconnect / recovery boundary

`AssignedSide(PlayerId)` resolves the current forced CS side from roster membership.
Native reconnect/team-lock adapters can use this without duplicating match policy.

`TournamentRecoverySnapshot` contains the state, selected maps, current map index,
series score, ready players, knife/side-choice ownership, current CS sides and paused
resume state. `Restore` validates the snapshot before accepting it.

This snapshot is a persistence contract, not yet a database implementation. Durable
storage, native team enforcement, veto integration, knife execution, demo recording,
round backups, coaching/spectator policy and real CS2 acceptance remain follow-up work
under #21.


## Durable recovery storage

The tournament persistence package stores match configuration and roster membership
separately from the validated recovery snapshot. One singleton runtime row points at
the currently active match, so activating a different match never requires deleting
historical match state.

Every stored match carries a positive revision. Snapshot saves require the caller's
expected revision and increment it transactionally; stale callbacks therefore fail
with `TournamentConcurrencyException` instead of overwriting newer state.

`TournamentRecoveryService` creates an active in-memory state machine from a durable
configuration, restores the active state after process restart, saves checkpoints and
deactivates completed or abandoned matches while preserving the final snapshot.
Configuration/roster replacement, snapshot writes and active-pointer updates are
transactional. The module-specific migration is applied through
`TournamentPersistenceBootstrap`.

This package still does not force CS teams or intercept join-team actions. Native team
locking/reconnect enforcement, veto presentation, demo recording and round backups
remain explicit follow-up work under #21.
