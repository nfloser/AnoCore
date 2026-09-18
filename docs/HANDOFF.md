# AnoCore development handoff

## Current workstream

- Full functional scope remains tracked in `docs/functional-acceptance.md`; umbrella #11 remains open.
- The shared AnoVeto module from issue #20 / PR #31 is merged on `main`.
- The centralized targeting/immunity foundation from issue #37 / PR #38 is merged on `main` at `ad73aa7cfea8ab4342e599cca8b65146e163551b`.
- Active feature work is issue #39 / draft PR #40 / branch `feature/39-custom-hud-anoveto`.
- Goal: replace AnoVeto's CenterHtml presentation with the reusable CounterStrikeSharp API-374 `CustomHudLayout` path while keeping generic `IMenuService` / CenterHtml available for modules that have not migrated.
- Extended administration #36 and other gameplay modules remain separate workstreams. They should consume the merged targeting/authorization foundation and must not be overwritten by this HUD work.

## Integrated shared foundations

- `RuntimeServices` now includes the merged shared player-target resolver and target-authorization service from PR #38.
- Future administration/moderation modules must consume those services rather than duplicate selector or immunity logic.
- This CustomHudLayout work does not alter targeting, immunity or administration semantics; the two workstreams only meet in the shared composition root.

## Custom HUD implementation

- `AnoCore.Abstractions.Hud` defines engine-independent `CustomHudId`, `CustomHudDefinition`, click context/handler and `ICustomHudService`.
- Definitions support both interactive surfaces with declared button IDs/input capture and information-only surfaces with no input capture. This is intentionally reusable for tournament brackets, match status, rankings and similar overlays.
- `AnoCore.Plugin.Hud.CounterStrikeCustomHudService` owns the CounterStrikeSharp `CCSCustomHudLayout` engine adapter.
- Desired HUD state is keyed by SteamID/player identity, not by slot.
- Per-player text variables, CSS classes, visibility and input capture are applied through API-374 extensions.
- Clicks are accepted only from the matching owned HUD entity, a declared button ID and a player for whom that HUD is currently visible.
- Slot-scoped engine state is reset when a client enters and desired state is reapplied after the client has initialized.
- HUD entities are recreated after map changes and desired visible state is reapplied.
- Registration/service cleanup hides tracked HUDs, releases input capture and removes owned entities, including registrations already queued for cleanup during unload/hot reload.

## AnoVeto migration

- `AnoVetoCommandController` now consumes `ICustomHudService` rather than `IMenuService`.
- `AnoVetoHudController` registers `panorama/layout/custom_game/anocore/ano_veto.xml`.
- `!anoveto create` still uses the existing shared map catalog, permissions, player registry and `IVoteService`; only presentation/input changed.
- Eight candidate maps are exposed as Panorama buttons `ano_veto_map_0` through `ano_veto_map_7`.
- Clicking a map routes into the existing coordinator/shared vote service. A successful ballot closes that player's HUD; completion closes it for everyone.
- The Close button releases the HUD/input without casting a ballot; bare `!anoveto` can reopen the active vote.
- Cancel and expiry hide the HUD for all tracked players.
- AnoVeto does not use CenterHtml for its voting UI on this branch.

## Panorama assets and deployment

- Source assets live under `ui/AnoCore`.
- `ui/AnoCore/build.ps1` copies the layout/style into an `anomeme_ui` CS2 addon, invokes Workshop Tools `resourcecompiler.exe`, and verifies the compiled `.vxml_c` / `.vcss_c` outputs.
- `-InstallLocalClient` additionally copies the compiled resources into the local CS2 client's `game/csgo/panorama/.../custom_game/anocore/` tree for a one-client smoke test without first publishing a Workshop addon.
- CI validates Panorama source files, required panel/button IDs and retention of the local-client test helper, but intentionally does not claim to execute Valve ResourceCompiler.
- The development artifact includes `ui/AnoCore` so the client addon can be compiled from the exact tested commit.
- For normal players, the compiled resources must be delivered as a client addon; a server plugin DLL alone cannot render the intended layout.

## Automated validation

- Initial custom-HUD CI exposed nullable API-extension calls; they were corrected.
- A subsequent complete run passed Release build and the 142-test suite, then exposed only final-newline formatting errors in two new test files; those were corrected.
- Review found and fixed an unload/hot-reload cleanup race so registrations queued for engine cleanup cannot be skipped when the whole HUD service is disposed.
- Cleanup now actively releases visibility/input for every tracked player state, including a player whose desired state was already set hidden but whose queued engine update had not executed yet.
- CI run #127 passed build, 142/142 tests including MariaDB integration, formatting, Panorama source validation, publish, deployment-package validation and artifact upload on the pre-main-integration code/assets.
- After merging current `main` into this branch, require a new full CI pass on the exact merge head before the real-server test.

## Real-server acceptance gate

Do not claim this feature production-ready or merge issue #39 solely from CI. On a disposable DatHost/CS2 test server with the matching client Panorama resources:

1. Verify AnoCore loads with CounterStrikeSharp API 374+ and the configured database.
2. Compile/install the local client resources with `ui/AnoCore/build.ps1 -InstallLocalClient`, restart CS2, then connect.
3. Start `!anoveto create` with at least eight configured maps.
4. Confirm the rich Panorama HUD appears with no CenterHtml vote UI.
5. Confirm input/cursor capture, eight clickable map buttons and Close behavior.
6. Verify one vote per SteamID, per-player visibility and reopen through bare `!anoveto`.
7. Test two clients, disconnect/reconnect and reused slots.
8. Test cancel, timeout, hot reload and unload; no orphaned HUD or stuck input may remain.
9. Complete a vote and verify exactly one real `changelevel` / `host_workshop_map` transition.

Record server/client versions and observations in issue #39 or PR #40 before merge.

## Integration rules

Use the shared services in `RuntimeServices`. Do not create a second player registry, permission evaluator, target resolver, target-authorization layer, database, command registry, menu service or vote service. Keep HUD presentation separate from vote/business logic. Engine calls after asynchronous work must be marshalled onto the server update thread. Preserve the API-374 guard, startup cancellation, connected-player bootstrap and unload cleanup.

License, NOTICE and source provenance must remain intact.
