# AnoCore development handoff

## Current workstream

- Full functional scope is defined in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Shared runtime composition from issue #34 / PR #35 is merged into `main` at `de160592a362c74f59b2a430829be62cce3de04f`.
- Active feature work: issue #20 / PR #31 / branch `feature/20-anoveto`.
- Extended administration commands remain a separate workstream and must not be overwritten.

## Known-good AnoVeto checkpoint

- Tested feature commit: `37c4ad5a0a9a432d208c781a61b31a1af091b216`.
- PR merge-test commit used by GitHub Actions: `e8000d55ca95bf56d6fb85d864e238aa333e8f07`.
- CI run: `35210019803` (#101).
- Restore: passed.
- Release build: passed with zero warnings and zero errors.
- Test suite: 122/122 passed, including MariaDB integration and AnoVeto lifecycle/runtime/configuration/eligibility tests.
- Formatting: passed.
- Development plugin publish: passed.
- Deployment package validation: passed and explicitly verifies `AnoCore.Modules.AnoVeto.dll` is present.
- Artifact upload: passed.

## AnoVeto implemented through native runtime composition

- Dedicated `AnoCore.Modules.AnoVeto` project with validated `anoveto.json` configuration.
- `maps.json` is loaded through the shared map catalog infrastructure; exactly eight unique candidates are selected through injectable randomness.
- Shared SteamID-based vote service is reused; AnoVeto does not duplicate permissions, voting, commands, menus, player state or persistence.
- `!anoveto create`, `!anoveto`, `status` and `cancel` logical command behavior is implemented.
- `css_anoveto` is bound through the existing CounterStrikeSharp command bridge.
- Logical eight-map menus are presented through the existing CounterStrike menu presenter / CenterHtml path.
- Menu selections route back into the same reconnect-safe vote session; one ballot per SteamID is enforced.
- Management authorization is inherited from the shared `ano.vote.manage` permission layer.
- Minimum-vote configuration greater than the eligible online population is rejected cleanly instead of throwing during vote construction.
- Deterministic quorum and tie handling is implemented.
- Votes finalize immediately once every eligible player voted, or on expiry/manual completion.
- A ballot submitted at the exact deadline is rejected without consuming the expired result, allowing the expiry owner to finalize exactly once.
- Map-change emission is guarded to occur at most once.
- Completed, cancelled and expired votes unregister the logical menu and close open menu sessions.
- Native runtime composition creates AnoVeto from the existing `RuntimeServices` and the real CounterStrike map changer.
- A repeating one-second CounterStrikeSharp timer finalizes expired AnoVeto sessions.
- Timer, controller, command registrations and shared runtime objects are disposed during unload/failed activation.
- AnoVeto configuration/composition failure is isolated: AnoCore continues without the optional module rather than failing the core runtime.
- The development package includes `AnoCore.Modules.AnoVeto.dll`; CounterStrikeSharp itself remains server-supplied.

## Open issues / acceptance gates

- A real CS2 server acceptance run is still required for actual CenterHtml rendering, player interaction and the final `changelevel` / `host_workshop_map` transition. CI cannot prove engine/UI behavior.
- PR #31 still requires final self-review, any resulting corrections, final CI on the exact reviewed head, ready-for-review transition and merge.
- Do not claim a production release or complete feature parity while umbrella #11 / release issue #23 and the remaining functional-acceptance rows are open.

## Next steps

1. Review the critical PR #31 paths: vote-service extension, coordinator concurrency/finalization, command/menu cleanup, configuration loading, host composition, expiry timer and packaging.
2. Apply only concrete review fixes, each as a small commit with a fresh CI gate.
3. Update PR #31 description to the completed scope and mark it ready for review.
4. Merge PR #31 only after the exact reviewed head is fully green.
5. After merge, choose the next independent workstream from `docs/functional-acceptance.md`; preserve other active branches and do not overwrite parallel administration work.
6. Perform the real-server AnoVeto acceptance before any production release claim.

## Integration rules

Use existing authorization, commands, menus, players, settings, voting and configuration services in `RuntimeServices`. Do not create a second database, permission system, command registry, menu service or vote service. Engine calls after asynchronous work must be marshalled onto the server update thread. Preserve the API-374 guard, startup cancellation, connected-player bootstrap and unload cleanup.

License, NOTICE and source provenance must remain intact.
