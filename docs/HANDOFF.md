# AnoCore development handoff

## Current workstream

- Full functional scope is defined in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Shared runtime composition from issue #34 / PR #35 is merged into `main` at `de160592a362c74f59b2a430829be62cce3de04f`.
- Active feature work: issue #20 / PR #31 / branch `feature/20-anoveto`.
- Extended administration commands remain a separate workstream and must not be overwritten.

## Known-good AnoVeto checkpoint

- Tested feature commit: `c19053b2e3e762ca91a244884dc54475311391f0`.
- CI run: `35208831026` (#90).
- Restore, Release build, complete test suite, formatting, development publish, deployment-package validation and artifact upload: passed.
- Previous lifecycle gate `aa47ca8d39aa14aa2602f61fc0023a6d1a111c8f` passed 117/117 tests; E2b adds two configuration tests.

## AnoVeto implemented through E2b

- Dedicated `AnoCore.Modules.AnoVeto` project.
- Eight unique configured maps selected through injectable randomness.
- Generic SteamID-based voting reused instead of duplicating a ballot engine.
- `!anoveto create`, `!anoveto`, `status` and `cancel` logical command behavior.
- Logical eight-map menu and ballot routing.
- Management authorization inherited from the shared vote permission layer.
- Reconnect-safe one-vote-per-SteamID behavior.
- Deterministic quorum and tie handling.
- Automatic map selection at timeout/manual completion and immediate completion once every eligible player voted.
- Deadline casts cannot consume the result before expiry finalization.
- Completed/cancelled/expired votes unregister logical menus and close open menu sessions.
- `AnoVetoConfiguration` provides validated defaults: enabled, 30-second duration, minimum one vote, deterministic option-order tie break.

## Next package: E2c native runtime composition

1. Compose AnoVeto from the already merged `RuntimeServices`; do not duplicate shared services.
2. Load `maps.json` through `MapCatalogLoader` and `anoveto.json` through `IConfigStore`.
3. Register AnoVeto before native command binding so `css_anoveto` is bound by the existing bridge.
4. After logical command dispatch, ask the existing `CounterStrikeMenuPresenter` to display any newly opened logical menu.
5. Add one repeating CounterStrikeSharp expiry timer and dispose it/controller cleanly on unload.
6. Ensure the plugin package contains the AnoVeto module assembly.
7. Run CI/review, then perform the documented real-server acceptance for actual CenterHtml interaction and map transition.

## Integration rules

Use existing authorization, commands, menus, players, settings, voting and configuration services in `RuntimeServices`. Do not create a second database, permission system, command registry, menu service or vote service. Engine calls after async work must be marshalled onto the server update thread. Preserve the API-374 guard, startup cancellation, connected-player bootstrap and unload cleanup.

## Remaining project scope

Complete every row in `docs/functional-acceptance.md`: remaining core/config/settings polish, administration/chat/tags/messaging (#17), stats/ranks/playtime/toplists (#18), extended commands, AnoVeto native acceptance (#20/#31), tournament (#21), SDK/API/integrations (#22) and end-to-end deployment/release (#23).

A real CS2 server acceptance run is still required for actual CenterHtml menu interaction and map transition behavior. Do not mark #11 or #23 complete or publish a production release while those acceptance gates remain open. License, NOTICE and source provenance must remain intact.
