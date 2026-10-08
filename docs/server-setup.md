# Server and database setup

This guide complements [quick installation](quick-install.md) and covers new installations and updates on an existing DatHost server.

## 1. Prepare the server

Create a CS2 server and install or verify Metamod and CounterStrikeSharp. AnoCore requires CounterStrikeSharp API 374 or newer with a compatible .NET 10 host. The plugin package does not include CounterStrikeSharp or MultiAddonManager. Use component builds compatible with your CS2 version. Stop the server and back up the plugin folder and database before updating.

DatHost provides file and console access. If its file manager starts inside `game`, omit that prefix from the paths below.

## 2. Prepare the database

AnoCore uses MySQL/MariaDB. Create a dedicated database and user; do not use the root account in the plugin. For managed databases, use the provider's host, port, database name, username and password. `localhost` is correct only if the database is actually reachable locally from the CS2 process.

For self-managed MariaDB, a database administrator can run:

```sql
CREATE DATABASE anocore CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'anocore'@'CS2_SERVER_HOST' IDENTIFIED BY 'REPLACE_WITH_STRONG_PASSWORD';
GRANT ALL PRIVILEGES ON anocore.* TO 'anocore'@'CS2_SERVER_HOST';
```

Replace `CS2_SERVER_HOST` with the host from which the database sees the server connection. Privileges are scoped to the dedicated database; `ALL PRIVILEGES` here includes schema changes required by current startup migrations. Do not grant privileges globally on `*.*`. Managed providers may expose this setup through their control panel. Allow database access only from the necessary server and configure TLS according to your provider's requirements. Never copy credentials into the repository, chat or logs.

## 3. Upload the plugin and configure its connection

1. Download and extract a matching successful CI build.
2. Stop the server. Upload all files from local `plugins/AnoCore` into `game/csgo/addons/counterstrikesharp/plugins/AnoCore`.
3. On a new installation, start once to generate `plugins/AnoCore/config/core.json`, then stop again.
4. Set `ConnectionString` in that file:

```json
{
  "ConnectionString": "Server=DB_HOST;Port=3306;Database=anocore;User ID=anocore;Password=YOUR_PASSWORD;Connection Timeout=10;Default Command Timeout=15",
  "PanoramaMenusEnabled": false
}
```

This is a setup excerpt, **not a replacement for an existing complete core.json**. Preserve all other generated properties. Alternatively, set `ANOCORE_MYSQL` in the server environment; it takes precedence over the file.

5. Start the server and inspect the console for database or loader errors. AnoCore applies its schema migrations during startup.
6. Run `css_anostatus` in the server console. `ready` confirms shared-service initialization, not complete acceptance of every module.

For `not configured`, check the connection string. For `startup failed`, investigate the specific startup error and restart after fixing it. For connection failures, check host, port, network access, user host restrictions and password. Do not delete tables to work around errors. See [deployment](deployment.md) for details and rollback.

## Addon and MultiAddonManager

The Workshop addon contains compiled Panorama layouts, CSS and the logo. It does not replace the server plugin. MultiAddonManager is a separate server component for additional Workshop addons; install it according to the instructions for your chosen build. [Upstream repository](https://github.com/Source2ZE/MultiAddonManager).

The existing ANOMEME server/MultiAddonManager CFG setup uses:

```text
mm_extra_addons "3815363712"
mm_addon_mount_download "1"
```

The first setting identifies the existing UI Workshop item. The second remains enabled in this setup for downloading on mount. Preserve other addon IDs. Use your own actual ID if publishing a separate item. The exact CFG file depends on the installation: search existing server CFGs for `mm_extra_addons` and `mm_addon_mount_download`. Do not create an arbitrary second configuration file without confirming it is loaded. These values do **not** belong in AnoCore's `core.json`.

First [build and upload the addon on Windows](steam-addon.md), then verify that the server and a clean client download/mount it. Only then set `"PanoramaMenusEnabled": true` in the existing `core.json` and restart. The plugin supplies team names, statistics and challenge values; updating these data does not require a Workshop upload. Layout, CSS and image changes do.

## Settings, permissions and tournaments

- Personal options: `!anosettingsmenu`; available options depend on loaded modules.
- Server configuration: `plugins/AnoCore/config`; edit generated files and use the documented [reload](configuration-reload.md) or required restart.
- Permissions: see [role administration](authorization.md); do not give every player administrative access.
- Tournaments: `!anotournamentload` creates a disabled `tournament-match` template if missing. Enter team names, captains and SteamID64 members, enable `Enabled` and load again. See [tournaments](tournament.md) for all fields and phases. No additional separate teams file is needed.

## Acceptance and updates

Restart the server and CS2. Check `!anostatus`, `!anomenu`, statistics, settings and veto. With two players, verify independent data, cursor release, reconnect and map changes. Check database persistence after a server restart. See the [full test plan](full-system-test.md).

For plugin updates, replace the full published binary set and **preserve config**. Back up the database first. For UI updates, build, update the same Workshop item and restart client/server. A plugin-only update does not require re-uploading an already matching UI.
