# AnoCore Development Handoff

This file is updated before major context or work-session boundaries so another development session can continue without guessing.

## Current main

- Repository: `nfloser/AnoCore`
- Main commit after shared-services merge: `80dcbc648219bd07fc9defcab4744f53a6217192`
- Completed workstreams: #1 bootstrap, #4 player lifecycle, #12 runtime event bus/CounterStrikeSharp composition, #13 shared services.
- Umbrella roadmap: #11.

## Active workstreams

### #14 — MySQL/MariaDB persistence

- Branch: `feature/14-persistence`
- Pull request: #26
- Latest implementation/review-fix commit: `5b1c40318997f21dba72bec301d79c86cdbe77a1`
- Previous green integration-test commit before review hardening: `ae81807d5f72ce01d2963e1499b6f6d67398691b`
- CI run #30 passed restore, Release build, MariaDB integration tests and format verification at `ae81807...`.
- Review found a stale-upsert bug where an old profile could regress `last_known_name` while `last_seen_utc` stayed monotonic.
- Regression test added in `9bb88f6186f0383f7e1b3a6924d1ae0c39b8d01b`.
- Fix added in `5b1c40318997f21dba72bec301d79c86cdbe77a1` so the name only advances with an equal/newer `last_seen_utc`.
- CI run #32 is pending/running for the hardened head and must be green before review completion/merge.

### #3 — Canonical GPLv3 license

- Branch: `feature/3-canonical-gplv3`
- Upstream reference: K4-Zenith `dev` `LICENSE.md` blob `a232fb906b309d40657bbeba71a1f8fecc0dc347`.
- Goal: replace the short notice with the verbatim GPLv3 license, preserve `NOTICE.md` attribution, verify the resulting license blob byte-for-byte, run CI, review and merge.

## Recently merged

### #13 — Shared services

- PR: #25
- Head: `3c8fad4afcf41b4ea2b164dc416ccb82de2dd669`
- Merge commit: `80dcbc648219bd07fc9defcab4744f53a6217192`
- CI: green (restore/build/tests/format).
- Self-review completed before merge.
- Provides typed JSON configuration, localization fallback, owner-bound placeholders and structured logging context.

### #12 — Runtime composition

- PR: #24
- Provides `AnoEventBus`, CounterStrikeSharp lifecycle hooks, hot-reload bootstrap/unload cleanup and documented real-CS2 verification procedure.
- CI green before merge.

## Open roadmap / dependency order

1. Finish #14 and #3.
2. Start #15 roles/permissions/groups/VIP/immunity from the new green `main`; persistence from #14 is required.
3. Continue #16 commands/menu/player settings after #15.
4. #17 admin/chat/messaging, #18 stats/ranks/playtime and #19 map catalog/generic voting can then progress as separate branches where dependencies permit.
5. Build #20 AnoVeto on #19/#15/#16 foundations.
6. Build #21 tournament/match on player/authorization/command/map foundations.
7. Complete #22 Web/API/server/security/SDK.
8. Finish #23 K4 migration closure, packaging, acceptance matrix, E2E verification and release.

## Known release gates / open problems

- Real native CS2 behavior cannot be claimed verified from GitHub CI alone. `docs/runtime-verification.md` remains a required real-server acceptance gate.
- #3 must be complete before any public distribution/release.
- Dependabot PRs #5–#9 remain open and should be rationalized/reviewed rather than blindly merged; #8/#9 overlap on MSTest.
- No production credentials may be committed; MariaDB CI uses disposable test credentials only.

## Next exact action

Check CI run #32 for `feature/14-persistence`. If green, finish independent PR #26 review and merge. If red, fetch the failing job log, fix only the concrete defect on the same branch and rerun. Then complete #3 and branch #15 from the updated green `main`.

## Project workstreams

See `docs/workstreams.md` and umbrella issue #11 for the complete workstream map.
