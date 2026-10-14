# AnoVeto create picker – implementation handoff

Tracking: #444; existing prerequisite design #374 and #375. This branch provides the **pure eight-map draft model and a catalog-backed explicit-pool coordinator entry point**, not the finished `!anoveto create` interface.

## Intended command and window flow

The `!anoveto create` command opens a session-owned ANOMEME dialog with three actions: **Create Veto**, **Random Veto**, **Use Veto**. The existing immediate random start remains available through the new `!anoveto random` command (added on this branch). For compatibility `!anoveto create` still starts immediately until the native create hub is integrated. Only an authorized player may manage or publish a vote.

- **Create Veto**: show sorted eligible catalog, map thumbnail/name, scroll controls and clear eight-map selection progress. On confirmation, pass a snapshot of exactly eight valid MapIds to the existing AnoVeto coordinator, not raw client-visible labels.
- **Random Veto**: start an independent random eight-map draw from the same eligible catalog.
- **Use Veto**: load owner-specific named eight-map presets from existing MariaDB; support preview, launch, rename, duplicate, edit and delete with transaction/version checks.
- Existing active voting HUD must remain independent; the three unique equal-weight selections from PR #383 belong to the ballot, **not** the eight-map picker.

## Current foundation

`AnoVetoMapDraft` filters and pages an eligible map catalog and rejects invalid IDs or ninth selections. A confirmed selection returns an isolated ordered snapshot. The draft does **not** persist presets, authorize players, instantiate the CustomHud or handle reconnection. `AnoVetoCoordinator.CreateSelectedAsync` resolves eight distinct requested MapIds against the current catalog and publishes them using the existing vote service and its authorization. It rejects missing/duplicate IDs and retains selected order. The command controller does not yet call this method. No server deployment or runtime behavior is claimed.

## Input acceptance tests required before removing page buttons

Test in CS2 with a rebuilt, matching `anomeme_ui` addon and two clients:

1. The window opens once, survives re-open, closes without stealing movement input, and does not modify active voting.
2. Mouse wheel scrolls a long catalog. Click/drag scrollbar, first/last item and navigation after map changes. Keep Prev/Next fallback until all pass.
3. Keyboard probe with real Panorama focus: type letters, Backspace, Escape, reconnect and ordinary chat/console commands. If unreliable, omit the text input; use alphabetical/category buttons and a separately implemented chat search.
4. Thumbnail beside each map, unique identity, missing-image fallback and no stale textures after closing.
5. Correct eight-map ownership and database preset CRUD; invalid/disabled map prompts replacement, never silent random substitution.
6. Confirm correct 3-selection public vote behavior from PR #383 and Casino controls from the exact same server/plugin+Workshop revision.

## Combined Casino test delivery

Do not publish this branch on its own. Integrate after validating #383, #374/#375 and the Casino stack #421/#438/#442; build one exact package with `BUILD-COMMIT.txt`, run all existing .NET/MariaDB/operator/PowerShell/Panorama CI checks and then verify native CS2. Preserve `core.json`, server database and Workshop item `3815363712`.

## Source resource staging (PR #445 follow-up)

The independent ANOMEME create landing screen is checked in as
`ano_veto_create.xml` and `ano_veto_create.css`. The combined
`ui/AnoCore/build.ps1` now compiles it alongside `ano_veto_cards`,
`ano_veto`, `menu` and `ano_tournament`, including local-client staging
when requested. `tests/operator/test_anoveto_create_resources.py` checks
the source tree and builder mapping.

**Not wired yet:** no `ICustomHudService.Register` registration, no
`anoveto create` command route, and no live/persistent preset CRUD. An XML
button by itself does not perform an action. The existing `anoveto create`
continues to start a random vote; `anoveto random` is an explicit alias
until the manager window is fully implemented. The search field remains
absent until keyboard capture is proven on real CS2 clients. Do not publish
or describe the staged layout as an end-user feature.

## Owner-scoped preset domain (next increment)

`AnoVetoPreset` now validates an immutable owner SteamID64, normalized printable
name and exactly eight case-insensitively distinct map IDs. Rename and map
replacement create new value objects, never mutate existing snapshots.
`AnoVetoPresetTests` covers defensive copying, identity, invalid data and
immutable updates.

This is intentionally not a database feature yet: no preset repository, schema
migration, owner-checked CRUD endpoint or clickable saved-preset listing is
wired. Preset map IDs must still be resolved against the current live catalog
via `CreateSelectedAsync` when used. No stored preset is ever assumed valid
merely because it was valid at creation time.

## Revisioned owner-scoped preset catalog

`AnoVetoPresetCatalog` adds a bounded 32-per-SteamID in-memory CRUD reference:
case-insensitive names, owner-filtered list/read, create, optimistic-revision
replace/rename and delete. Wrong owners cannot change entries; conflicting
versions and name collisions reject rather than silently overwrite.

**Important:** this class is an in-memory foundation, **not durable storage**.
No MariaDB migration/repository has been introduced. Production use must
replace the in-memory storage boundary with a transactional database adapter,
verify permissions/session ownership at each caller, restore after restart,
and resolve every saved MapId against the live eligible catalog.
`AnoVetoPresetCatalogTests` covers isolation, stale revisions and limits.

## Repository adapter contract

`IAnoVetoPresetRepository` now defines cancellation-aware asynchronous list/read,
create, replace and delete operations. Each write must be atomic, owner-scoped
and revision-checked. `AnoVetoPresetCatalog` implements the same contract for
offline unit tests, but is still **in-memory only**.

Before server use, introduce the concrete MySQL/MariaDB adapter and a retry-safe
migration using existing AnoCore persistence conventions, hook the adapter into
plugin composition and run MariaDB integration tests (including races, rollback,
restart recovery and revision conflicts). Merely satisfying the interface in an
in-memory catalog does not persist any records across restarts.

## Native create landing screen integration

`!anoveto create` now opens the registered `ano.veto.create` CustomHud rather than immediately starting a vote. Clicking **Random Veto** invokes the existing server-authoritative `AnoVetoCoordinator.CreateAsync`, retaining the normal `ano.vote.manage` permission and public voting UI. The explicit `!anoveto random` command remains an immediate fast path. Closing the create HUD makes no vote. The **Create Veto** and **Use Veto** buttons currently display a clear not-yet-integrated message, not a fake successful action.

The command itself currently exposes the non-mutating landing screen before permission verification; the Random click is checked by `VoteService`. Native UI access control and complete per-session creation lifecycle must be hardened before production release.

This is not the finished picker: the map browser, owner-persisted presets, scrolling/keyboard and three-vote feature branch remain to be integrated and verified together with the Casino HUD fix.

## Manual map-picker wiring

The `ano.veto.create` HUD now contains eight map-preview cards, a selection
counter, Prev/Next buttons, Back and Confirm. A host clicking **Create Veto**
enters a per-player draft populated from `AnoVetoCoordinator.GetAvailableMaps`.
Map preview classes reuse the existing compiled `ano_veto_previews.vcss_c`
and map names come from the current server catalog. Selected cards are
highlighted. **Start Selected Veto** requires eight unique maps and calls
`AnoVetoCoordinator.CreateSelectedAsync` through the normal server-authorized
vote service. `tests/AnoCore.Tests/AnoVeto/AnoVetoCommandControllerTests.cs`
includes the end-to-end virtual HUD click path.

The paged navigation is an intentional fallback; true wheel-scroll and keyboard
search are still native CS2 acceptance items. **Use Veto** remains unconnected
until durable MariaDB repository integration. Neither click-contract tests nor
Panorama source assets establish live Workshop input behavior. The #383
three-vote patch is also not included in this branch yet.
