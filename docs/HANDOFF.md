# AnoCore development handoff

## Current workstreams

- Full functional scope remains in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Targeting/immunity issue #37 / PR #38 is merged in main at `ad73aa7cfea8ab4342e599cca8b65146e163551b`.
- Persistent moderation issue #41 / PR #42 is merged in main at `fd20c543ece4a95f8d1978a1a5a97bb40dc3f97d`.
- Active UI work: issue #39 / draft PR #40 / branch `feature/39-custom-hud-anoveto`.
- Extended administration #36 and remaining #17 packages are separate workstreams and must consume the shared targeting/moderation foundations.

## Integrated administration foundations

Main now provides one shared:
- `IPlayerTargetResolver`
- `ITargetAuthorizationService`
- `IModerationRepository`
- `IModerationService`

Future admin/moderation/extended-command modules must reuse these services. Do not duplicate selector, immunity, sanction or audit state.

## Custom HUD implementation

- `AnoCore.Abstractions.Hud` defines engine-independent HUD IDs, definitions, click context/handlers and `ICustomHudService`.
- Definitions support interactive and information-only surfaces.
- `CounterStrikeCustomHudService` owns the CounterStrikeSharp API-374 `CCSCustomHudLayout` adapter.
- Desired state is keyed by player/SteamID rather than slot.
- Text variables, CSS classes, visibility and input capture are applied per player.
- Clicks are accepted only from the matching owned layout/entity, declared button and visible player.
- Slot reuse resets engine state before reapplying desired state.
- Map changes recreate owned HUD entities and restore desired visible state.
- Registration/service cleanup releases visibility/input and removes owned entities, including queued cleanup during unload/hot reload.

## AnoVeto CustomHud migration

- AnoVeto now consumes `ICustomHudService` instead of `IMenuService` for its vote UI.
- It registers `panorama/layout/custom_game/anocore/ano_veto.xml`.
- Eight map candidates map to buttons `ano_veto_map_0` through `ano_veto_map_7`.
- Existing map catalog, permissions, player registry and shared `IVoteService` remain authoritative.
- Successful map clicks route through the existing coordinator; completion/cancel/timeout hides HUD state.
- Close releases the HUD/input without voting; bare `!anoveto` can reopen an active vote.
- Generic `IMenuService` / CenterHtml remains available for modules not migrated to CustomHud.

## Panorama assets and deployment

- Source assets live under `ui/AnoCore`.
- `ui/AnoCore/build.ps1` builds the layout/style through local CS2 Workshop Tools and checks compiled outputs.
- `-InstallLocalClient` supports a one-client development smoke test.
- CI validates source structure/panel IDs and packages the exact UI source tree but does not claim to run Valve ResourceCompiler.
- Normal clients need the compiled addon delivered/mounted; the server DLL alone cannot provide arbitrary Panorama XML/CSS.

## Automated status

- Pre-main-integration feature head `34e1546e715b6bbf9150414a92bd0fb888c39305` passed CI #133 with 142/142 tests, formatting, Panorama validation, publish, package validation and artifact upload.
- Review fixes already cover nullable API extension calls and HUD cleanup races during queued cleanup/unload.
- This branch has now been merged with current main so it includes the persistent moderation foundation as well.
- Require a fresh exact-head CI pass after this merge commit before any runtime acceptance claim.

## Real-server acceptance gate for #39

Keep PR #40 draft and issue #39 open until a real CS2/DatHost test with matching client Panorama resources verifies:

1. AnoCore loads with CounterStrikeSharp API 374+ and the configured database.
2. The compiled AnoCore client UI is installed/mounted.
3. `!anoveto create` presents the rich Panorama HUD with no CenterHtml vote UI.
4. Cursor/input capture, Close and all eight map buttons work.
5. One vote per SteamID and independent per-player visibility/reopen work.
6. Two-client behavior, reconnect and reused slots work.
7. Cancel, timeout, map change, hot reload and unload leave no orphan HUD/stuck input.
8. Vote completion emits exactly one real `changelevel` / `host_workshop_map` transition.

Record server/client versions and observations in #39/#40 before merge.

## Next steps

1. Run full CI on the new main-integration head of PR #40.
2. Keep #40 draft until the real-server CustomHud acceptance above is recorded.
3. In parallel, continue #17 from current main in small independent packages: native moderation enforcement, then permissioned admin commands/UI.
4. Keep #36 Extended Commands separate until its shared #17 dependencies are merged.
5. Preserve test-first commits, exact-head CI, self-review and this handoff before context boundaries.

## Integration rules

Use shared services from `RuntimeServices`. Keep HUD presentation separate from voting/business logic. Online admin commands must resolve/authorize targets centrally before moderation changes. Engine calls after asynchronous work must be marshalled onto the server update thread. Preserve API-374 guards, startup cancellation, player bootstrap and unload cleanup.

License, NOTICE and source provenance must remain intact.
