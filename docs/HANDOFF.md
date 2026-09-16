# AnoCore Development Handoff

This file is updated before major context or work-session boundaries so another development session can continue without guessing.

## Current workstream

- Issue: #20 — Implement AnoVeto map vote module
- Branch: `feature/20-anoveto`
- Base `main`: `7d8333d61aa47106db39bed293a7123661d10aae`
- Previous completed workstream: #19 / PR #30 — map catalog and generic voting infrastructure

## Completed foundation

The following independently reviewable workstreams are merged to `main`:

- #1 / PR #2 — core contracts, module lifecycle, solution structure and CI.
- #4 / PR #10 — reconnect-safe player lifecycle.
- #12 / PR #24 — event bus and CounterStrikeSharp runtime composition.
- #14 / PR #26 — MySQL/MariaDB persistence and migrations.
- #15 / PR #27 — deterministic roles, permissions and immunity.
- #16 / PR #28 — commands, menus and player settings.
- #3 / PR #29 — byte-verified canonical GPLv3 license text.
- #19 / PR #30 — map catalog and generic voting infrastructure.

## Latest tested state

PR #30 head before merge: `032e49607c59b9239b52c3a2dfe8bc5779afab7e`.

CI run 55 (`35145795972`) passed completely:

- MariaDB service initialization: passed.
- `dotnet restore AnoCore.sln`: passed.
- Release build: passed with 0 warnings and 0 errors.
- Full test suite: 92 tests passed, including MariaDB integration tests and map/voting tests.
- `dotnet format AnoCore.sln --verify-no-changes --no-restore`: passed.

The first PR #30 run exposed formatting-only failures. They were corrected mechanically by `dotnet format` in commit `4cd6c4f0b03d3809691e4ea1dadd47cc5f2d221c`; the temporary formatting workflow was then removed in `032e49607c59b9239b52c3a2dfe8bc5779afab7e`. The PR received an explicit self-review and was merged as `7d8333d61aa47106db39bed293a7123661d10aae`.

## Current AnoVeto acceptance target

Issue #20 requires:

- `!anoveto create` restricted by AnoCore authorization.
- `!anoveto` opens a visual menu for eligible players.
- each created vote selects exactly 8 unique configured maps.
- random selection is injectable/deterministic in tests.
- each eligible SteamID may vote exactly once, including across reconnects.
- timeout, no-vote, tie, disconnect and reconnect behavior is deterministic.
- the winning catalog entry resolves to its engine/workshop ID and triggers map loading exactly once.
- management supports status/cancel as appropriate.
- unit/integration tests and documentation are complete.
- real CS2 verification covers menu interaction and actual map transition before release readiness is claimed.

## Open roadmap workstreams

- #17 — admin, chat tags and messaging.
- #18 — statistics, ranks, playtime and toplists.
- #20 — AnoVeto (current branch).
- #21 — tournament and competitive match orchestration.
- #22 — Web/API, server management, security and developer SDK.
- #23 — K4 migration closure, packaging, real-server E2E verification and first release.
- #11 — umbrella roadmap remains open until all child workstreams and release gates are complete.

Dependabot PRs #5–#9 are also open and must be reconciled before release; overlapping MSTest update PRs should not be merged blindly.

## Known external/release gates

1. Native CounterStrikeSharp/CS2 behavior cannot be considered verified merely because managed CI passes. Real-server checks remain required for menu rendering, game event timing, hot reload/unload and map transitions.
2. No release should be tagged until #23 has a recorded real-server acceptance matrix and migration inventory.
3. There are no known blocking managed-code defects on current `main` at this checkpoint.

## Next steps

1. Define AnoVeto behavior with tests first on `feature/20-anoveto`.
2. Implement the module using existing authorization, command, menu, map catalog and vote services rather than duplicating them.
3. Commit tested functional slices regularly, update docs, open PR, run CI, self-review, correct findings and merge.
4. Continue #17 and #18 as isolated workstreams from a known-green `main`; #21 follows once #18/#20 dependencies are merged.
5. Complete #22, then #23 and the final real-server/release acceptance work.
6. Refresh this file after every merged workstream or before any context boundary.

## Project workstreams

See `docs/workstreams.md` and umbrella issue #11 for dependency ordering and parallelizable streams.
