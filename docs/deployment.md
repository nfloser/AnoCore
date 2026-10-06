# Development deployment

This package is for verifying the current AnoCore runtime on a test server. It is not a finished replacement for the server's gameplay plugins.

## Requirements

- CS2 with Metamod and CounterStrikeSharp installed.
- CounterStrikeSharp API 374 or newer, with a .NET 10-compatible host. The current development artifact is built against API 1.0.376.
- A successful AnoCore CI run for the exact commit being installed.

An older server hosting other working plugins does not establish compatibility with this build. Inspect the installed CounterStrikeSharp version first.

## Install

1. Open the successful GitHub Actions CI run and download `AnoCore-development`.
2. Extract it locally. Keep `BUILD-COMMIT.txt`, `LICENSE.md`, `NOTICE.md`, `INSTALL.md` and `AnoCore-source.zip` with the deployment record.
3. Stop the test server and back up any existing `game/csgo/addons/counterstrikesharp/plugins/AnoCore` directory.
4. Copy the complete `plugins/AnoCore` directory into `game/csgo/addons/counterstrikesharp/plugins/`. The result must include `AnoCore/AnoCore.dll`, `AnoCore.Runtime.dll`, `AnoCore.Abstractions.dll`, `AnoCore.Modules.Admin.dll`, `AnoCore.Modules.Stats.dll`, `AnoCore.Modules.AnoVeto.dll`, `AnoCore.Modules.Tournament.dll`, `AnoCore.Modules.Tournament.Persistence.dll`, `AnoCore.deps.json` and the published dependency DLLs.
5. Do not copy a private `CounterStrikeSharp.API.dll` into this directory.
6. Configure the database as described below, then restart. Confirm the shared-services-ready message and no loader exceptions.
7. Run `css_anostatus` in the server console, then connect a human player and run `!anostatus`.

Copying only `AnoCore.dll` is insufficient. CI publishes the project dependency graph and checks the required files before uploading.

## What is integrated in the full-system test package

The integration candidate includes the shared runtime plus the currently code-complete native stacks in one artifact:

- reconnect-safe player lifecycle, profiles, permissions, commands, menus, settings and configuration reloads;
- persistent moderation with connect-ban enforcement, mute/gag/silence handling, audited kick/silent-kick and warnings;
- persistent playtime, kill/death/assist statistics, deterministic toplists and configurable ranks;
- audited rank point administration, rank transition notifications and rank/menu views;
- warmed native chat formatting, colors, rank/tag placeholders and permission-gated selectable tags;
- self-service player toggle commands/menu;
- persisted tournament match recovery plus session-safe roster team assignment enforcement;
- AnoVeto using the Panorama CustomHud path plus live veto/map configuration reloads;
- an opt-in authenticated local management pipe that reuses the management HTTP contract without adding a web server to the plugin;
- the prerelease `AnoCore.Abstractions` module SDK under `sdk/`.

The plugin initializes migrations, module data, player profiles, authorization, command/menu services, settings, placeholders and voting only after successful database startup. These features are assembled for disposable-server verification; CI proves the automated gates, not native CS2/DatHost behavior. Use `full-system-test.md` for the exact real-server acceptance pass before treating the candidate as production-ready.

## Database configuration

On first load, AnoCore creates `plugins/AnoCore/config/core.json` with an empty `ConnectionString`. Set that value to your dedicated MySQL/MariaDB connection string, or configure `ANOCORE_MYSQL` in the server environment (environment takes precedence). Never commit production credentials. Restrict access to this configuration file.

Example structure, with values supplied by the operator:

```json
{
  "ConnectionString": "Server=127.0.0.1;Database=anocore;User ID=anocore;Password=YOUR_DATABASE_PASSWORD;Connection Timeout=10;Default Command Timeout=15"
}
```

Use a dedicated database; startup applies the existing AnoCore schema migrations. Startup has a 30-second cancellation deadline. Missing configuration or a database/authorization failure leaves only lifecycle tracking and status available; privileged services are not activated. Correct configuration and restart to retry.

## Optional Leetify context

Internal `!anorating` requires no external service. To additionally enable the
separate live `!anoleetify <player>` command, create a Leetify developer API key
and expose it to the server process as `ANOCORE_LEETIFY_API_KEY`. Do not add the
key to `core.json`, source control, startup arguments that are visible to other
users, or log output.

When the variable is missing, AnoCore does not register the external command and
makes no Leetify requests. When enabled, lookups occur only when a user explicitly
runs `!anoleetify`; returned API data is not persisted. The integration is
bounded to the official HTTPS API endpoint, a three-second timeout, 128 KiB per
response and two concurrent requests.

Leetify currently requires attribution/link-back and prohibits storing or
recalculating its API metrics. Its 2026 privacy policy change also means the
Public API may return no profile for players who are not registered with Leetify.
Review the current Leetify Public API and Developer Guidelines before enabling
this optional integration in a release.

## Local management bridge

AnoCore also creates `plugins/AnoCore/config/management.json`. It is disabled by
default and is intentionally separate from `core.json`. Leave it disabled unless a
trusted local management client or the future HTTPS sidecar is being tested.

If enabled, configure only hashed management credential material in this file; never
store the bearer secret there. The bridge is a same-user local named pipe and does not
open a TCP/HTTP listener. See `docs/management-api.md` for credential provisioning,
the pipe frame contract, scopes, rate limits and the requirements for any network
sidecar.

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
- Management bridge when enabled: authenticated health succeeds, a wrong secret is rejected, unload closes the pipe and hot reload can bind the same pipe name again.
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
