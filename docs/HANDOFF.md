# AnoCore development handoff

## Support workstream — 2026-09-17

- Issue: #32.
- Branch: `fix/deployment-readiness`.
- Source change: `7f86ad77378a58da050a42138530ab67cb21f09c`.
- Packaging change: `089bc0e4aceb410eaaffd7b8710d89ec2764ce1e`.
- Existing independent feature work: #31, branch `feature/20-anoveto`, observed head `e2a137fab59192ad11488cb27d2837e9bb2f3425`. Recheck its current head before integrating.
- This support branch does not implement or overwrite the feature module.

## Changes

- Require API 374, matching the pinned compile-time API.
- Bootstrap connected humans on every load, including manual load without a hot-reload flag.
- Ignore queued player-refresh callbacks after registry replacement.
- Register/remove `css_anostatus` with lifecycle and explicitly disclose incomplete gameplay composition.
- Publish the complete plugin dependency graph; validate required files and reject a private engine API DLL.
- Include install instructions, exact source archive, commit identifier and license notices in the development artifact.
- Update active product documentation to describe AnoCore directly; preserve provenance and license notices.

## Verified source-level finding / next integration task

`AnoCorePlugin.Load` currently creates only `AnoEventBus` and `PlayerRegistry`. Available persistence, authorization, command, menu, settings, map and vote classes are not wired into a complete runtime. The same composition gap was observed on #31's recorded head.

Coordinate the composition work with #31. Initialize configuration/persistence/authorization, attach feature modules, bind native commands and dispose the bindings on unload. Check server-thread affinity after async operations, initialization rollback, command conflicts and fail-closed authorization.

The standalone vote plugin was inspected only as a reference. No code there was changed by this support workstream.

## Validation

No local .NET SDK or native CS2 runtime is available in this session. Build, tests (including MariaDB), formatting and publishing must pass the support PR's CI. Native load, command, UI and map-change behavior require the test-server checks in `docs/deployment.md` and `docs/runtime-verification.md`.

This file is not a claim of green CI or production readiness. Consult the support PR checks for the exact tested commit and result. No release should be tagged until #23's real-server acceptance gate passes.
