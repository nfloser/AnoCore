# ANOMEME player dashboard

`!anomenu` opens the personal dashboard. `!anomenunavigation` opens the existing
feature navigation directly. The native Home button returns to the dashboard.
The dashboard contains name/rank, lifetime XP/level, kill/death/assist totals,
playtime and the first two visible challenges with their current progress,
base reward and deadline. It displays actual registered read-command results;
missing/disabled or forbidden modules show `Unavailable`. It never substitutes
example values. Challenge previews follow the challenge module's existing order.
`All challenges` opens the full existing challenge view, `Open menu` opens the
existing destinations, and `Refresh dashboard` queries the values again.
Data refreshes on open/refresh, not continuously. The text-menu fallback also
shows all six summary rows and dashboard actions. Navigation remains filtered
by registered commands and permissions; optional feature modules are not required.

## Deployment for Nille (Windows + DatHost)

Use the `AnoCore-development` artifact from a successful CI run containing the
dashboard integration. See the [quick installation guide](quick-install.md)
and [Workshop addon guide](steam-addon.md). Do not use an
earlier release: it does not populate dashboard panels.

1. Download and extract the development package from GitHub Actions.
2. Stop the DatHost server. Back up `game/csgo/addons/counterstrikesharp/plugins/AnoCore`
   (or `csgo/addons/...` if the file manager starts at `game`).
3. Upload **all files** from the package's `plugins/AnoCore` into that server folder,
   replacing existing packaged binaries. Preserve its existing `config` directory
   and database settings. Do not replace CounterStrikeSharp or MultiAddonManager.
4. In the server's AnoCore `config/core.json`, retain `"PanoramaMenusEnabled": true`.
5. On your Windows PC, extract the package's `AnoCore-source.zip` into a new folder.
   Open that folder in VS Code (it contains `AnoCore.sln` and `ui`). Save edits with
   Ctrl+S, then Terminal → New Terminal → PowerShell. Run from that folder:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\ui\AnoCore\build.ps1 -Addon anomeme_ui
   ```

   This force-compiles both menu and veto CSS/XML plus the logo. It copies source
   files into CS2 `content/csgo_addons/anomeme_ui/panorama`, and produces compiled
   assets under `game/csgo_addons/anomeme_ui/panorama`. All compilation must succeed.
   Windows CS2 Workshop Tools must be installed; compilation cannot be done on DatHost.
6. Open CS2 Workshop Tools with the existing `anomeme_ui` addon. Open the publisher,
   choose the existing **AnoCore UI** item **3815363712**, and upload/update its
   **content**. Editing its Workshop description does not upload the rebuilt files.
   Keep the same Workshop item/ID. Check upload completion and any approval status.
7. Retain MultiAddonManager `mm_extra_addons "3815363712"`. Use
   `mm_addon_mount_download "1"` when refreshing the server copy, then restart the
   server. Restart CS2 fully, reconnect and enter **`!anomenu` in game chat**.
   `ano.menu` is the internal HUD identifier, not a console command.
8. Verify the cards show your data, `Open menu` shows the existing feature entries,
   `All challenges` opens the full view, Refresh updates values and Close releases
   the cursor. Test with a second player: their profile/progress must stay separate.
   Test disconnect/reconnect and other menus while a read is pending.

Native acceptance is still required: CI cannot run Valve's Windows resourcecompiler
or inspect the actual CS2 client. Record the plugin build and Workshop upload used.
If visuals are still old, confirm content upload completed before clearing caches.

## Where to edit later

- Layout/panel order: `ui/AnoCore/layout/custom_game/anocore/menu.xml`.
- Colors, sizes, spacing: `ui/AnoCore/styles/custom_game/anocore/menu.css`.
- Original logo: `ui/AnoCore/styles/custom_game/anocore/anomeme_banner.png`.
  Preserve its `.vtex` descriptor; the build compiles it too.
- Dashboard sources, action order, navigation destinations:
  `src/AnoCore.Runtime/Menus/AnoHomeMenuModule.cs`.
- Native panel text/button mapping:
  `src/AnoCore.Runtime/Menus/PanoramaMenuPresenter.cs`.
- Challenges: server `plugins/AnoCore/config/challenges.json`.
  Validate edits and use its supported configuration reload or restart; progress
  comes from the database. Reopen/refresh the dashboard afterwards.
- Tournament configuration belongs to the existing tournament module/server config;
  this dashboard does not introduce a separate teams file or new tournament editor.

For a CSS/XML/logo-only change, save → run the same PowerShell build → upload content
to item 3815363712 → refresh server addon/restart client. No C# rebuild is needed.
For a C# change, build/test/publish the updated plugin as well, upload its full package,
and restart the server. Do not merely copy C# source or one DLL onto DatHost.

## Back and fullscreen backdrop

The footer separates `Back` (parent view) from `Prev page` / `Next page` (pagination).
Back is active on the first page of every submenu. It uses the feature's existing
`back` or `home` action; feature roots without one return to navigation. Navigation
returns to the dashboard. Challenges opened directly from the dashboard also return
to the dashboard. Only the dashboard itself has no parent, so Back is disabled there.
The green translucent backdrop covers the full anonymous host/overlay (100% width
and height); only the centered window has a fixed size. Verify coverage in CS2 on
both normal and ultrawide screens, because native parent sizing cannot be tested here.

## Clean native navigation and operator access

The native presenter keeps Previous/Next/Back actions in the footer rather than repeating them as content rows. Headings omit source-page suffixes. Settings use six content rows per source page. Challenge summaries do not offer pages beyond their reported count. Dashboard playtime is rounded down to whole days, hours and minutes; detailed state breakdowns remain available through the playtime command.

Admin destinations require AnoCore permissions. Server ownership alone does not grant a player role. In the **server console**, use your actual SteamID64:

```text
css_anoroledefine owner 100 ano.*
css_anograntrole YOUR_STEAMID64 owner 0
css_anoroles YOUR_STEAMID64
```

This grants broad permanent administrative access to the specified account; use narrower roles for other staff. Never paste another player's ID by accident. Reopen the menu after granting permissions. The sidebar backdrop adjustment still requires real-client verification, especially at unusual aspect ratios.

Personal stats also show headshot kills as a percentage of kills and recorded match wins as a percentage of recorded wins plus losses. Missing denominators display an em dash; rounds are not used as match results.
