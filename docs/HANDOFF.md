# AnoCore development handoff

## Current workstream

- Full functional scope remains tracked in `docs/functional-acceptance.md`.
- The shared AnoVeto module from issue #20 / PR #31 is merged on `main`.
- Active feature work is issue #39 / draft PR #40 / branch `feature/39-custom-hud-anoveto`.
- Goal: replace AnoVeto's CenterHtml presentation with the reusable CounterStrikeSharp API-374 `CustomHudLayout` path while keeping generic `IMenuService` / CenterHtml available for modules that have not migrated.
- Extended administration and other gameplay modules remain separate workstreams and must not be overwritten.

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
- CI validates Panorama source files and required panel/button IDs but intentionally does not claim to execute Valve ResourceCompiler.
- The development artifact includes `ui/AnoCore` so the client addon can be compiled from the exact tested commit.
- The compiled Panorama resources must be available to clients. A server plugin DLL alone cannot render the intended custom layout.

## Automated validation

- The first custom-HUD branch CI exposed nullable API-extension calls; they were corrected.
- The next complete run reached a successful Release build and successful test suite, then failed only the formatting gate because two new test files lacked final newlines; those files were corrected.
- Additional review found and fixed an unload/hot-reload cleanup race so registrations queued for engine cleanup cannot be skipped when the whole HUD service is disposed.
- Cleanup now actively releases visibility/input for every tracked player state, including a player whose desired state was already set hidden but whose queued engine update had not executed yet.
- Final CI must pass build, all tests including MariaDB integration, formatting, Panorama source validation, publish, deployment-package validation and artifact upload on the exact branch head before server testing.

## Real-server acceptance gate

Do not claim this feature production-ready or merge issue #39 solely from CI. On a disposable DatHost/CS2 test server with the matching client Panorama addon:

1. Verify AnoCore loads with CounterStrikeSharp API 374+ and the configured database.
2. Start `!anoveto create` with at least eight configured maps.
3. Confirm the rich Panorama HUD appears with no CenterHtml vote UI.
4. Confirm input/cursor capture, eight clickable map buttons and Close behavior.
5. Verify one vote per SteamID, per-player visibility and reopen through bare `!anoveto`.
6. Test two clients, disconnect/reconnect and reused slots.
7. Test cancel, timeout, hot reload and unload; no orphaned HUD or stuck input may remain.
8. Complete a vote and verify exactly one real `changelevel` / `host_workshop_map` transition.

Record server/client versions and observations in issue #39 or PR #40 before merge.

## Integration rules

Keep shared authorization, commands, players, voting, map catalog, persistence and configuration services authoritative. Do not create a second vote system or database inside the HUD layer. Engine calls after asynchronous work must be marshalled to the server update thread. Preserve the API-374 guard, startup cancellation, connected-player bootstrap and unload cleanup.

License, NOTICE and source provenance must remain intact.
