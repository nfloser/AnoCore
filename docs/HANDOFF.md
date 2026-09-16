# AnoCore Development Handoff

This file is updated before major context or work-session boundaries so another development session can continue without guessing.

## Current workstream

- Issue: #16 — Implement command system, menu UI and player settings
- Branch: `feature/16-command-menu-settings`
- Pull request: #28 — Implement command, menu and player settings services
- Latest tested implementation commit: `abd5ed7d834aa4b712dd515f86799cfe63824c29`
- Base `main` when the workstream started: `78c6e9cb275b856b093cd4ba1999970334595b5a`

## Implemented

- Module-owned async command registry with disposable registrations and `UnregisterAll` cleanup.
- Consistent `ano` command/alias namespace enforcement.
- Quoted command-token parsing with escaped characters.
- Typed command arguments (`String`, `Int32`, `UInt64`, `Boolean`) with required/optional validation before feature handlers run.
- Generated/custom command usage metadata plus `GetCommands()` discovery for help/API surfaces.
- Permission evaluation before player command handlers execute.
- Explicit command result/failure model; handler failures are contained instead of escaping into game callbacks.
- CounterStrikeSharp command bridge mapping logical `anoxxx` commands to native `css_anoxxx`, enabling `!anoxxx` and `/anoxxx` chat usage.
- CounterStrikeSharp-independent menu registry with module ownership, duplicate protection and logical open-menu state.
- One-shot menu selection by default; explicit `keepOpen` support for repeat interactions.
- CounterStrikeSharp `CenterHtmlMenu` presenter so native framework pagination/navigation can render AnoCore logical menus.
- Typed per-player settings with defaults and persistence through `IModuleDataStore` under a validated dedicated settings namespace.
- `MenuId` implemented as a validated reference type so an invalid `default(struct)` identifier cannot bypass construction rules.
- Module-author documentation in `docs/commands-menus-settings.md`.

## Commit history for this workstream

1. `50d77fe901cca865e73020b007e4893ebe01b1eb` — test: define command menu and settings behavior
2. `ae045871a365e3a31b8fefa2c3b9b4ac966f364d` — test: enforce ano command namespace
3. `29652045c37910c275c17e412b6148b80270f764` — feat: implement command menu and settings services
4. `dd0ff9ec03709c43e5b8c640a604fbbf87aa7bac` — feat: add CounterStrikeSharp command and menu adapters
5. `7863da0a63d5c9644adaf427fc951409bc4d1c6d` — fix: align player settings key and harden menu IDs
6. `5d60248e0045112fe62b2126d38075634e4caf2e` — fix: avoid menu callback discard shadowing
7. `b0b0875f6d113e33c0c72ed449a9b97e3f88983b` — test: define typed command arguments and help metadata
8. `abd5ed7d834aa4b712dd515f86799cfe63824c29` — feat: add typed command arguments and help metadata

## Test / CI status

- PR #28 CI run 45 (`35140324922`) completed successfully on implementation head `abd5ed7d834aa4b712dd515f86799cfe63824c29`.
- MariaDB service initialization: passed.
- `dotnet restore AnoCore.sln`: passed.
- Release build: passed.
- Full test suite, including database integration tests and new command/menu/settings tests: passed.
- `dotnet format --verify-no-changes`: passed.
- Two earlier CI findings were fixed in-branch rather than ignored: the PlayerId property mismatch and a CounterStrikeSharp menu callback discard-shadowing compile error.

## Review status

- Scope was checked against issue #16 after the first green run; missing typed argument parsing/help metadata was found and implemented test-first before merge.
- Menu identifiers were hardened from a struct to a validated reference type.
- Core command/menu/settings logic remains CounterStrikeSharp-independent; CSS code stays under `AnoCore.Plugin`.
- Command and menu registrations support deterministic unload cleanup.
- No known blocking source-level finding remains on the tested implementation head.
- A final PR review and CI run including this handoff-only commit are still required before merge.

## Parallel / independent workstreams

- #3 GPLv3 compliance is isolated on `chore/3-canonical-gpl-license`, now reset to current green `main`. The upstream K4-Zenith GPLv3 blob SHA is `a232fb906b309d40657bbeba71a1f8fecc0dc347`. A first manual copy produced a different SHA and was intentionally not committed. Only a byte-identical blob may close #3.
- After #16 is merged, #17 Admin/Messaging, #18 Stats/Ranks and #19 Map Catalog/Generic Voting can branch independently from the same green `main`.
- #22 Web/API/developer SDK can also progress once the #16 contracts are merged, but it should consume stable contracts rather than duplicate command/menu/authorization logic.
- #20 AnoVeto depends on #19; #21 Tournament depends on the relevant permission/interaction/stats/map/veto foundations.

## Open problems / release gates

1. Real CounterStrikeSharp/CS2 server behavior has not been executed in this GitHub CI environment. Native command registration, CenterHtml rendering, event timing and full unload/hot-reload behavior remain real-server acceptance checks.
2. #3 canonical GPLv3 content is still open and must be complete before public distribution or a release.
3. No release should be cut until #23 end-to-end/migration closure is satisfied.

## Next steps

1. Run CI on this handoff commit, perform explicit PR #28 self-review, fix any findings and merge #16.
2. Branch #17, #18 and #19 independently from the resulting main and develop them with separate TDD commits/PRs.
3. Keep #3 isolated and only commit the canonical GPLv3 file after exact hash verification.
4. Continue the dependency chain into #20, #21 and #22, then finish #23 with real-server end-to-end acceptance and release documentation.
5. Update this handoff after every merged workstream or before any context boundary.

## Project workstreams

See `docs/workstreams.md` and umbrella issue #11 for dependency ordering and parallelizable streams.
