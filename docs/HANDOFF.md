# AnoCore development handoff

## Current workstream

- Full functional scope is defined in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Shared runtime composition from issue #34 / PR #35 is merged into `main` at `de160592a362c74f59b2a430829be62cce3de04f`.
- Active feature work: issue #20 / PR #31 / branch `feature/20-anoveto`.
- Extended administration commands remain a separate workstream and must not be overwritten.

## Known-good AnoVeto checkpoint

- Reviewed implementation head before this documentation-only checkpoint: `42794bcebb7a7b833052b08d59890597fdffe3f8`.
- CI run: `35225883916` (#104).
- Restore: passed.
- Release build: passed.
- Test suite: 122/122 passed, including MariaDB integration and AnoVeto lifecycle/runtime/configuration/eligibility tests.
- Formatting: passed.
- Development plugin publish: passed.
- Deployment package validation: passed and explicitly verifies `AnoCore.Modules.AnoVeto.dll` is present.
- Artifact upload: passed.
- Review finding resolved: native CenterHtml menus are now tracked by instance and reconciled against logical menu state, so timeout/cancel/finalization does not leave stale AnoVeto UI visible or accidentally close a later unrelated native menu.

## AnoVeto implemented through native runtime composition

- Dedicated `AnoCore.Modules.AnoVeto` project with validated `anoveto.json` configuration.
- `maps.json` is loaded through the shared map catalog infrastructure; exactly eight unique candidates are selected through injectable randomness.
- Shared SteamID-based vote service is reused; AnoVeto does not duplicate permissions, voting, commands, menus, player state or persistence.
- `!anoveto create`, `!anoveto`, `status` and `cancel` logical command behavior is implemented.
- `css_anoveto` is bound through the existing CounterStrikeSharp command bridge.
- Logical eight-map menus are presented through the existing CounterStrike menu presenter / CenterHtml path.
- Native menu instances are reconciled after command dispatch, selections and expiry, while guarding against closing menus that replaced the tracked AnoVeto instance.
- Menu selections route back into the same reconnect-safe vote session; one ballot per SteamID is enforced.
- Management authorization is inherited from the shared `ano.vote.manage` permission layer.
- Minimum-vote configuration greater than the eligible online population is rejected cleanly instead of throwing during vote construction.
- Deterministic quorum and tie handling is implemented.
- Votes finalize immediately once every eligible player voted, or on expiry/manual completion.
- A ballot submitted at the exact deadline is rejected without consuming the expired result, allowing the expiry owner to finalize exactly once.
- Map-change emission is guarded to occur at most once.
- Completed, cancelled and expired votes unregister the logical menu and reconcile native open menu instances.
- Native runtime composition creates AnoVeto from the existing `RuntimeServices` and the real CounterStrike map changer.
- A repeating one-second CounterStrikeSharp timer finalizes expired AnoVeto sessions.
- Timer, controller, command registrations and shared runtime objects are disposed during unload/failed activation.
- AnoVeto configuration/composition failure is isolated: AnoCore continues without the optional module rather than failing the core runtime.
- The development package includes `AnoCore.Modules.AnoVeto.dll`; CounterStrikeSharp itself remains server-supplied.

## Review status and acceptance gates

- Critical PR paths reviewed: generic vote extension, coordinator finalization/concurrency, command/menu cleanup, configuration loading, shared-service composition, CounterStrikeSharp host integration, expiry timer and packaging.
- No remaining code-review blocker is known after the stale-native-menu fix.
- A real CS2 server acceptance run is still required for actual CenterHtml rendering, key interaction and the final `changelevel` / `host_workshop_map` transition. CI cannot prove engine/UI behavior.
- Do not claim a production release or complete feature parity while umbrella #11 / release issue #23 and the remaining functional-acceptance rows are open.

## Next steps

1. Require final CI on the exact documentation checkpoint head, then mark PR #31 ready and merge it.
2. After merge, preserve the real-server AnoVeto acceptance as a release gate.
3. Select the next independent workstream from `docs/functional-acceptance.md`; preserve other active branches and do not overwrite parallel administration work.
4. Continue using small test-first commits and update this handoff before context boundaries.

## Integration rules

Use existing authorization, commands, menus, players, settings, voting and configuration services in `RuntimeServices`. Do not create a second database, permission system, command registry, menu service or vote service. Engine calls after asynchronous work must be marshalled onto the server update thread. Preserve the API-374 guard, startup cancellation, connected-player bootstrap and unload cleanup.

License, NOTICE and source provenance must remain intact.
