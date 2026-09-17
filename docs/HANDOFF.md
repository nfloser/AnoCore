# AnoCore Development Handoff

This file is updated before major context or work-session boundaries so another development session can continue without guessing.

## Current workstream

- Issue: #20 — Implement AnoVeto map vote module
- Branch: `feature/20-anoveto`
- Pull request: #31 (draft)
- Base `main`: `7d8333d61aa47106db39bed293a7123661d10aae`
- Current implementation head before this checkpoint: `3d1fc6f38167e8b4f696c58337a9c6eca0f9f918`

## Completed foundation

Merged to `main`: #1/#2 core and CI; #4/#10 player lifecycle; #12/#24 event/runtime composition; #14/#26 MariaDB persistence; #15/#27 permissions/immunity; #16/#28 commands/menus/settings; #3/#29 canonical GPLv3; #19/#30 maps and generic voting.

Last known-green main-derived CI was PR #30 run 55 (`35145795972`): restore, Release build, 92 tests including MariaDB integration, and format verification all passed.

## AnoVeto block plan

A. Coordinator compiles and its test-first contract passes.
B. `!anoveto create` command composition and authorization.
C. `!anoveto` visual menu and vote selection.
D. status/cancel management behavior.
E. CounterStrikeSharp runtime composition and expiry ticking.
F. documentation and real-server acceptance checklist.
G. final full CI, self-review, fixes and merge.

Each block must be committed independently before the next block starts.

## Current Block A

Implemented test-first contracts for:
- exactly eight unique selected maps using injectable randomness;
- catalogs with fewer than eight maps rejected;
- one ballot per SteamID across reconnect identity;
- deterministic quorum/tie handling through the generic vote service;
- timeout finalization;
- cancellation without map change;
- winning map change emitted at most once.

Commits:
- `ac4b004562bfd4f248ec852f50cc066157e58cac` — red tests for AnoVeto coordinator.
- `f33f8694c185ab461556bd8655be921c66092568` — initial AnoVeto coordinator implementation.
- `e2a137fab59192ad11488cb27d2837e9bb2f3425` — align tests with `PlayerId.SteamId64` API.
- `3d1fc6f38167e8b4f696c58337a9c6eca0f9f918` — align coordinator with `VoteResult.WinningOptionId` API.

CI run 58 (`35146422195`) against `e2a137f` restored successfully but failed compilation only because the coordinator still referenced the old `WinnerOptionId` name. That compile defect is fixed in `3d1fc6f`.

## Open roadmap workstreams

- #17 — admin, chat tags and messaging.
- #18 — statistics, ranks, playtime and toplists.
- #20 — AnoVeto (current).
- #21 — tournament and competitive match orchestration.
- #22 — Web/API, server management, security and developer SDK.
- #23 — K4 migration closure, packaging, real-server E2E verification and first release.
- #11 — umbrella roadmap.

## External/release gates

Native CounterStrikeSharp/CS2 behavior is not considered verified by managed CI alone. Real-server checks remain required for menu rendering, runtime event timing, hot reload/unload and actual map transitions. No release is tagged until #23 records the acceptance matrix and migration inventory.

## Next exact step

Run normal PR CI on the post-compile-fix checkpoint. Do not start Block B until Block A builds and all existing/new AnoVeto tests pass. If CI is green, record the result here and then begin Block B with tests first.
