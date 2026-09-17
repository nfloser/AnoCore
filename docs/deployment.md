# Development deployment

This package is for verifying the current AnoCore runtime on a test server. It is not a finished replacement for the server's gameplay plugins.

## Requirements

- CS2 with Metamod and CounterStrikeSharp installed.
- CounterStrikeSharp API 374 or newer, with a .NET 10-compatible host.
- A successful AnoCore CI run for the exact commit being installed.

An older server hosting other working plugins does not establish compatibility with this build. Inspect the installed CounterStrikeSharp version first.

## Install

1. Open the successful GitHub Actions CI run and download `AnoCore-development`.
2. Extract it locally. Keep `BUILD-COMMIT.txt`, `LICENSE.md`, `NOTICE.md`, `INSTALL.md` and `AnoCore-source.zip` with the deployment record.
3. Stop the test server and back up any existing `game/csgo/addons/counterstrikesharp/plugins/AnoCore` directory.
4. Copy the complete `plugins/AnoCore` directory into `game/csgo/addons/counterstrikesharp/plugins/`. The result must include `AnoCore/AnoCore.dll`, `AnoCore.Runtime.dll`, `AnoCore.Abstractions.dll`, `AnoCore.deps.json` and the published dependency DLLs.
5. Do not copy a private `CounterStrikeSharp.API.dll` into this directory.
6. Restart the server. Confirm the AnoCore lifecycle-loaded message and no loader exceptions.
7. Run `css_anostatus` in the server console, then connect a human player and run `!anostatus`.

Copying only `AnoCore.dll` is insufficient. CI publishes the project dependency graph and checks the required files before uploading.

## What works in this package

- Engine player lifecycle hooks and reconnect-aware registry.
- Bootstrap of already-connected humans on manual load and hot reload.
- Read-only status command reporting the tracked human count.
- Old queued refresh callbacks are ignored after registry replacement.

The plugin currently does not compose database/configuration, authorization, commands/menus, voting or optional gameplay modules into a complete runtime. Database settings and feature commands are therefore not advertised as working. The integrated AnoVeto work is tracked separately in #20/#31; avoid registering its command concurrently with another plugin.

## Verification

Record the commit from `BUILD-COMMIT.txt`, installed host version and observed results:

- Empty server: status returns zero tracked humans.
- Join/disconnect: count changes correctly; bots and HLTV are excluded.
- Manual plugin load with humans already connected: the existing humans are tracked.
- Hot reload: no duplicate registrations; status still works.
- Team changes, spawn and death: no errors or stale-state exceptions.
- Unload: status command and event handlers are removed.
- Reload twice: no repeated callbacks or plugin errors.

See the repository's `docs/runtime-verification.md` for the broader acceptance checklist. CI does not run a native CS2 server.

## Rollback

Stop the server, replace only the AnoCore plugin directory with its backup, then restart. Preserve unrelated plugins and server configuration. This development package does not migrate or overwrite an existing gameplay database.

## Fastest path to feature readiness

1. Complete the composition root: load validated configuration, initialize persistence/migrations, load authorization and connect command/menu adapters.
2. Attach the independently developed feature modules with explicit shutdown/rollback.
3. Register each native command once; ensure engine operations run on the server thread after asynchronous database work.
4. Exercise permissions, UI, disconnect/reconnect, timeout and map transition with real clients.
5. Record server evidence before marking the feature or release production-ready.
