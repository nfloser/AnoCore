# AnoCore Development Handoff

This file is updated before major context or work-session boundaries so another development session can continue without guessing.

## Current workstream

- Issue: #4 — Implement player lifecycle and player service registry
- Branch: `feature/4-player-lifecycle`
- Pull request: #10 — Implement reconnect-safe player lifecycle
- Latest tested implementation commit: `e3606339f61c00970726daf27d910f660fbc61c5`

## Implemented

- CounterStrikeSharp-independent player snapshots and IDs.
- Reconnect-safe per-connection session IDs.
- In-memory player registry with connect/reconnect/update/disconnect semantics.
- Stale-session protection for delayed disconnects and updates.
- Lifecycle events and thin CounterStrikeSharp mapper boundary.
- Human-player filtering at the CSS boundary.
- Tests for connect, reconnect, stale disconnect/update handling and validation.
- Disconnect snapshots are explicitly `IsConnected = false` and `IsAlive = false`.
- Repository workstream decomposition and persistent handoff process.

## Test status

- Earlier CI run 11 exposed an ambiguous MSTest assertion; fixed in `aea32f0b278c0717567774e10b9a401dbce64af4`.
- Self-review then found that disconnected snapshots could remain alive.
- Regression test commit `49ecde213f7ea3aeb44f944d19de977121287980` captures that case.
- Runtime fix commit `e3606339f61c00970726daf27d910f660fbc61c5` marks disconnected players dead.
- CI run 16 on the fix completed successfully: restore, Release build, tests and format verification all passed.

## Review status

- Architecture boundary reviewed: core/runtime remain CounterStrikeSharp-independent.
- Reconnect/session semantics reviewed.
- Disconnect-state review finding fixed and regression-tested.
- No remaining blocking findings known at this checkpoint.

## Open items / next steps

1. Submit the explicit PR #10 self-review and merge after this handoff-only commit is green.
2. Start #12 on `feature/12-runtime-event-bus` from the new `main`.
3. Implement/test the event bus before wiring CounterStrikeSharp hooks.
4. In parallel, #3 remains isolated on `chore/3-canonical-gpl-license`; it must be completed before public distribution.
5. Continue roadmap issues #13–#23 in dependency order described in `docs/workstreams.md` and #11.

## Project workstreams

See `docs/workstreams.md` and umbrella issue #11 for dependency ordering and parallelizable streams.
