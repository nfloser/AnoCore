# AnoCore development handoff

## Current workstream

- Full functional scope is defined in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Shared runtime composition from issue #34 / PR #35 is merged into `main` at `de160592a362c74f59b2a430829be62cce3de04f`.
- Active feature work: issue #20 / PR #31 / branch `feature/20-anoveto`.
- Extended administration commands remain a separate workstream and must not be overwritten.

## Known-good AnoVeto checkpoint

- Tested feature commit: `d9e08bc38c2f69076c1980e3b0955ae1badc20dc`.
- CI run: `35208276343`.
- Restore: passed.
- Release build: passed with zero compile errors.
- Test suite: 115/115 passed, including MariaDB integration tests.
- Formatting: passed.
- Development plugin publish: passed.
- Deployment package validation and artifact upload: passed.

## AnoVeto implemented through E1

- Dedicated `AnoCore.Modules.AnoVeto` project.
- Eight unique configured maps selected through injectable randomness.
- Generic SteamID-based voting reused instead of duplicating a ballot engine.
- `!anoveto create`, `!anoveto`, `status` and `cancel` command behavior.
- Logical eight-map menu and ballot routing.
- Management authorization inherited from the shared vote permission layer.
- Reconnect-safe one-vote-per-SteamID behavior.
- Deterministic quorum and tie handling.
- Automatic map selection at timeout or manual completion.
- Vote now finalizes immediately when every eligible player has voted.
- A cast exactly at the deadline is rejected without consuming the expired result, so the expiry owner can still finalize it exactly once.
- Map change remains guarded to execute at most once.

## Next package: E2 native runtime composition

1. Compose AnoVeto from the already merged `RuntimeServices` rather than creating duplicate services.
2. Load the map catalog and AnoVeto options from validated configuration.
3. Register the AnoVeto command before native command binding, then bind it through the existing `CounterStrikeCommandBridge`.
4. Present logical AnoCore menus through `CounterStrikeMenuPresenter` after command dispatch.
5. Add a small repeating CounterStrikeSharp timer that calls AnoVeto expiry and removes stale vote menus.
6. Dispose the controller/timer cleanly on unload and preserve startup cancellation/thread-marshalling guarantees.
7. Cover CounterStrikeSharp-independent composition/lifecycle behavior with tests before host wiring.

## Integration rules

Use the existing authorization, commands, menus, players, settings, voting and configuration services in `RuntimeServices`. Do not create a second database, permission system, command registry, menu service or vote service. Engine calls after asynchronous work must be marshalled onto the server update thread. Preserve the API-374 guard, startup cancellation, connected-player bootstrap and unload cleanup.

## Remaining project scope

Complete every row in `docs/functional-acceptance.md`: remaining core/config/settings polish, administration/chat/tags/messaging (#17), stats/ranks/playtime/toplists (#18), extended commands, AnoVeto native acceptance (#20/#31), tournament (#21), SDK/API/integrations (#22) and end-to-end deployment/release (#23).

A real CS2 server acceptance run is still required for actual CenterHtml menu interaction and map transition behavior. Do not mark #11 or #23 complete or publish a production release while those acceptance gates remain open. License, NOTICE and source provenance must remain intact.
