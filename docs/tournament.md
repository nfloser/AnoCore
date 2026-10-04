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


## Team assignment enforcement

When tournament persistence starts successfully, the plugin restores the single active
match into `TournamentMatchRuntime`. Roster membership then becomes authoritative for
T/CT assignment while that match is active.

`TournamentTeamEnforcement` listens to connected, reconnected and player-state update
events. A rostered player whose tracked team differs from the active match assignment
is scheduled back to the assigned side. Matching players and non-rostered players are
left untouched.

Enforcement is bound to the current `PlayerSessionId`, deduplicates an in-flight
correction per session and owns a lifetime cancellation token. The native transport
rechecks the current registry session immediately before calling CounterStrikeSharp
`SwitchTeam` on the server update thread. This prevents a delayed correction from
moving a replacement session and prevents the correction's own team update from
forming a feedback loop.

Hot reload/startup reconciles already connected rostered players after the persisted
active match is restored. Unload cancels scheduled corrections and removes event
subscriptions.

Real `jointeam` timing, reconnect timing and team-change event ordering remain a
disposable-server acceptance gate. The current adapter corrects the resulting tracked
team change; a stricter pre-command interception can be added later if live testing
shows a visible bypass window.


## Match definition and controls

Tournament setup is loaded from the fixed `tournament-match` configuration. The
command layer never accepts an arbitrary filesystem path. If the file does not exist,
`anotournamentload` creates a disabled template; edit that generated configuration,
set `Enabled` to `true`, then run the load command again.

Example shape:

```json
{
  "Enabled": true,
  "MatchId": "22222222-2222-2222-2222-222222222222",
  "BestOf": 3,
  "KnifeRound": true,
  "OvertimeEnabled": true,
  "TeamA": {
    "Name": "Alpha",
    "Tag": "A",
    "CaptainSteamId": 76561198000000001,
    "Members": [76561198000000001, 76561198000000002]
  },
  "TeamB": {
    "Name": "Beta",
    "Tag": "B",
    "CaptainSteamId": 76561198000000011,
    "Members": [76561198000000011, 76561198000000012]
  }
}
```

The definition validates BO1/BO3/BO5, printable bounded team metadata, non-zero unique
SteamIDs, captain membership and cross-team roster overlap.

The management permission is `ano.tournament.manage`. The server console can perform
management operations without a player permission grant. Supported controls are:

- `anotournamentload`: validate, persist and activate the configured match;
- `anotournamentstatus`: bounded current state, map, series score and ready progress;
- `anotournamentreadyopen`: move Setup to Ready;
- `anoready`: current rostered player marks their SteamID ready;
- `anotournamentmaps <map1,map2,...>`: commit the already-resolved BO map series after both rosters are ready;
- `anotournamentknife <a|b>`: record the knife-round winner;
- `anotournamentside <t|ct>`: knife-winning team's connected captain chooses the starting side;
- `anotournamentpause`, `anotournamentresume`, `anotournamentovertime`;
- `anotournamentmapwin <a|b>`: advance the series and deactivate automatically on the winning map;
- `anotournamentabandon [reason]`: clear the active pointer while retaining the last durable snapshot.

All state-changing commands are serialized. They clone the currently published state,
apply the transition to the clone, write it with the expected durable revision, and
publish the new runtime state only after persistence succeeds. A revision conflict
reloads the active durable state and asks the operator to retry.

Privileged state changes use requested/completed administrative audit records. If the
request audit fails, persistence is not attempted. If the durable state succeeds but
the completion audit fails, the runtime still adopts the persisted state and the
command reports the audit failure explicitly instead of leaving memory behind the
database.

## Demo and round-backup lifecycle

`TournamentMatchCaptureService` owns the match-scoped demo/backup boundary without
exposing arbitrary server commands to modules. Native integrations implement
`ITournamentMatchCaptureTransport` with four typed operations: start/stop the current
demo and capture/restore a known round backup.

Demo ids and backup names are generated from the validated match id, map index, round
and recovery revision. Callers cannot inject paths or raw command fragments.

All operations are serialized. A command must present the current recovery revision;
stale callbacks fail with `TournamentConcurrencyException` before transport work.
Round capture/restore is restricted to live, paused or overtime states. Restores are
limited to backups from the same match and current map, while a bounded in-memory
catalog keeps deterministic newest-first metadata.

Starting twice for the same map is idempotent. Starting the next map stops the
previous owned demo before a new one is started. Async unload waits for an in-flight
start/capture operation and then stops any owned demo best-effort. Transport cleanup
failure is observable through the supplied error callback but does not pretend to
roll back durable tournament state.

The CounterStrikeSharp adapter still needs disposable-server validation for the fixed
demo/round-backup commands and actual generated backup filename semantics. No generic
server-command execution is part of this module contract.
