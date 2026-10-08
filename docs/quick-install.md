# Quick installation: Windows and DatHost

For server operators. The server plugin and Workshop addon are separate updates. Use a successful CI build containing the dashboard version you want. `BUILD-COMMIT.txt` records its source revision. Older builds cannot populate new dashboard panels. No battle pass is included.

## Requirements

A CS2 server with Metamod, CounterStrikeSharp API 374 or newer and a compatible .NET 10 host; a dedicated MySQL/MariaDB database. UI compilation requires Windows, CS2 and CS2 Workshop Tools. See [server/database setup](server-setup.md) and [deployment](deployment.md) for prerequisites and rollback.

## Install or update the server plugin

1. Open the matching successful GitHub Actions CI run, download `AnoCore-development` and extract it.
2. Stop the DatHost server and back up its existing AnoCore folder, including configuration.
3. **Source on your PC:** the contents of `plugins\AnoCore` in the extracted package.
4. **Destination on DatHost:** `game/csgo/addons/counterstrikesharp/plugins/AnoCore`. If the file manager starts inside `game`, the visible path begins with `csgo/addons/...`.
5. Replace all published program files. Do not create a nested `AnoCore/AnoCore` folder. **Keep the existing `config` directory**, especially `core.json` and its connection string. Exclude packaged configuration files when updating.
6. On a new installation, the first startup creates `config/core.json`. Stop the server, enter your database connection string and start it again. Never commit credentials.
7. For the native UI, set `"PanoramaMenusEnabled": true` in the existing `config/core.json` after addon delivery is configured.
8. Do not replace CounterStrikeSharp or MultiAddonManager with files from this package.

Copying only `AnoCore.dll` is insufficient: all published dependencies belong together.

## Install or update the Workshop addon

See the full [Steam addon and logo guide](steam-addon.md). From the repository/package folder containing `ui`, run in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\ui\AnoCore\build.ps1 -Addon anomeme_ui
```

After successful compilation, update the existing addon through Workshop Manager: **Re-Upload → anomeme_ui → Submit**. The ANOMEME installation retains Workshop ID `3815363712`. Other operators use their own item ID. Check the existing MultiAddonManager settings:

```text
mm_extra_addons "3815363712"
mm_addon_mount_download "1"
```

Preserve other configured addon IDs. These settings belong to the server/MultiAddonManager configuration, not AnoCore's `core.json`. The exact CFG file depends on the existing server setup.

## Start and verify

Wait for upload/approval, start the server and completely restart CS2. Server console: `css_anostatus`. Game chat: `!anostatus`, then `!anomenu`. Check the dashboard, logo, challenge previews, Open menu, Back, Home, pagination and Close. Back is disabled only on the dashboard; Prev page is disabled on page 1. Test with two players to confirm separate personal values.

Already updated the Workshop addon? Update only the matching server plugin. Changed only CSS/XML/logo? Rebuild and upload the UI; no C# build is needed. To restore an earlier version, stop the server and restore the backed-up program files and matching UI. Handle database-schema rollback separately as described in [deployment](deployment.md).
