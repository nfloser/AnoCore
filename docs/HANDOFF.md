# AnoCore development handoff

## Current workstream

- Full functional scope is defined in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Runtime composition: issue #34, PR #35, branch `feature/runtime-composition`.
- Prior deployment work: #33 merged.
- Independent feature work: #31, `feature/20-anoveto`. Do not overwrite that branch.
- Extended administration commands: #36, separate from core/runtime.

## Implemented in this branch

- Async shared-service startup with database probe, migrations and authorization load before native activation.
- Configuration through `ANOCORE_MYSQL` or `plugins/AnoCore/config/core.json`.
- 30-second startup deadline and cancellation on unload; failed/missing configuration does not activate privileged commands.
- Shared module service provider for events, players, profiles, data, authorization, commands, menus, settings, placeholders and votes.
- Player profile persistence on connect/reconnect/disconnect/name change and bootstrap of humans present during startup.
- Native `anocommands` and permission-gated `anoreloadauth`, plus honest `anostatus`.
- World-update command replies guarded against unload and changed player identity.
- Integration correction: settings keys now use dots, not colons rejected by the MySQL store.

## Validation evidence and remaining checks

- Initial test commit `7f03aea1a600ff47d543a73e29ad1990baa14353` failed because the composition implementation did not yet exist (CI 35201860967).
- The first implementation CI (35202043099) built successfully but reproduced an existing settings/MySQL key mismatch through the new restart test.
- After the fix, CI 35202374205 passed all 96 tests including MariaDB composition/restart checks; its formatting gate found two import-order issues.
- This commit corrects those imports. Consult PR #35 for the final CI run on the exact head, including packaging.
- No native CS2 server is available in this session. Startup/unload, real menu interaction and map transitions remain server acceptance gates; no production release is claimed.

## Integration contract for the feature workstream

The plugin exposes `Runtime` (the `RuntimeServices` container) and `MenuPresenter` after successful initialization. Use its existing authorization/commands/menus/players/settings services rather than duplicating repositories or connection logic. New feature commands need explicit native binding and unload cleanup; current startup binds the core descriptors. Optional feature modules are not automatically discovered or loaded by this slice.

Preserve the API-374 guard, unconditional connected-player bootstrap, startup cancellation and stale-refresh guard when integrating. Engine calls after asynchronous work must be marshalled onto the server update thread. Update status/module reporting only for actual loaded modules.

## Still required for full functionality

Complete every row in `docs/functional-acceptance.md`: core registration/settings UI/config upgrades, administration/chat/tags/messaging (#17), stats/ranks/playtime/toplists (#18), extended commands (#36), custom vote (#20/#31), tournament (#21), SDK/API/integrations (#22) and end-to-end deployment/release (#23).

Do not mark #11 or #23 complete, call this feature parity, or publish a production release while these rows remain open. License/NOTICE and source provenance remain intact.
