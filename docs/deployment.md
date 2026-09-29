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
6. Configure the database as described below, then restart. Confirm the shared-services-ready message and no loader exceptions.
7. Run `css_anostatus` in the server console, then connect a human player and run `!anostatus`.

Copying only `AnoCore.dll` is insufficient. CI publishes the project dependency graph and checks the required files before uploading.

## What works in this package

- Engine player lifecycle hooks and reconnect-aware registry.
- Bootstrap of already-connected humans on manual load and hot reload.
- Read-only status command reporting the tracked human count.
- Old queued refresh callbacks are ignored after registry replacement.

The plugin initializes migrations, module data, player profiles, authorization, command/menu services, settings, placeholders and the vote service. Native core commands are bound only after successful database startup. Optional modules still require their individual integration and server acceptance.

## Database configuration

On first load, AnoCore creates `plugins/AnoCore/config/core.json` with an empty `ConnectionString`. Set that value to your dedicated MySQL/MariaDB connection string, or configure `ANOCORE_MYSQL` in the server environment (environment takes precedence). Never commit production credentials. Restrict access to this configuration file.

Example structure, with values supplied by the operator:

```json
{
  "ConnectionString": "Server=127.0.0.1;Database=anocore;User ID=anocore;Password=YOUR_DATABASE_PASSWORD;Connection Timeout=10;Default Command Timeout=15"
}
```

Use a dedicated database; startup applies the existing AnoCore schema migrations. Startup has a 30-second cancellation deadline. Missing configuration or a database/authorization failure leaves only lifecycle tracking and status available; privileged services are not activated. Correct configuration and restart to retry.

`css_anocommands` lists registered logical commands. `css_anoreloadauth` reloads persisted role assignments. `css_anoconfigs` lists module configurations that have adopted the reload registry, and `css_anoreloadconfig <name>` reloads one such configuration. The server console is allowed; players require `ano.core.reload` for every reload/configuration-inspection command. No player receives this permission by default. See `docs/authorization.md` for the persisted authorization model and `docs/configuration-reload.md` for reload guarantees.

Connected/reconnected/disconnected profiles and name changes are stored through the real player repository; player settings use storage-safe keys and survive restart. Unit tests alone are supplemented by MariaDB composition tests. No legacy database is automatically imported.

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

Stop the server, replace only the AnoCore plugin directory with its backup, then restart. Preserve unrelated plugins and server configuration. This package applies AnoCore schema migrations to the configured database. Back up that database before an upgrade; do not point it at an unrelated gameplay database.

## Fastest path to feature readiness

1. Verify configured database startup and existing core commands on the target host.
2. Attach the independently developed feature modules with explicit shutdown/rollback.
3. Register each native command once; ensure engine operations run on the server thread after asynchronous database work.
4. Exercise permissions, UI, disconnect/reconnect, timeout and map transition with real clients.
5. Record server evidence before marking the feature or release production-ready.
