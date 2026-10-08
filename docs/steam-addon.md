# Steam Workshop addon: overlay and logo

For AnoVeto map pictures, first prepare the configured map catalog using
[anoveto-map-previews.md](anoveto-map-previews.md), then add -PreviewSource to
the build command. The addon must contain the compiled preview textures and
stylesheet as well as the layout; missing previews display neutral cards.

This guide covers the Windows build for `anomeme_ui` and updates to the existing ANOMEME Workshop item. Source assets and logo belong in the repository; use a revision containing the dashboard assets when building that UI.

## Repository files

| Asset | Path |
| --- | --- |
| Logo banner | `ui/AnoCore/styles/custom_game/anocore/anomeme_banner.png` |
| Texture descriptor | `ui/AnoCore/styles/custom_game/anocore/anomeme_banner.vtex` |
| Menu layout | `ui/AnoCore/layout/custom_game/anocore/menu.xml` |
| Menu styling | `ui/AnoCore/styles/custom_game/anocore/menu.css` |
| Veto layout | `ui/AnoCore/layout/custom_game/anocore/ano_veto.xml` |
| Veto styling | `ui/AnoCore/styles/custom_game/anocore/ano_veto.css` |
| Build script | `ui/AnoCore/build.ps1` |

Keep existing panel/button IDs: the server plugin uses them. The dashboard build copies the PNG and descriptor into the addon and creates the `.vtex_c` resource used by the menu.

## 1. Open the folder

Clone/download the repository or extract the CI package. In VS Code, open the folder directly containing `ui`. Alternatively, extract `AnoCore-source.zip` and open its root. Select Terminal → New Terminal → PowerShell, then check:

```powershell
Test-Path .\ui\AnoCore\build.ps1
```

Expected: `True`. Save edits with Ctrl+S first.

## 2. Build the addon

```powershell
powershell -ExecutionPolicy Bypass -File .\ui\AnoCore\build.ps1 -Addon anomeme_ui
```

The script locates CS2 in Steam libraries. The dashboard script force-compiles logo, menu and veto resources. For a different installation, supply the actual path, for example:

```powershell
powershell -ExecutionPolicy Bypass -File .\ui\AnoCore\build.ps1 -Addon anomeme_ui -Cs2 "D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"
```

Upload only after `AnoCore Panorama HUD compiled successfully.` appears without compile errors. If `resourcecompiler.exe` is missing, install CS2 Workshop Tools. If step 1 returns `False`, open the correct folder. The dashboard build needs no manual deletion of old resources. Omit `-InstallLocalClient` for normal Workshop testing: local resources can mask the actual Workshop version.

## 3. Local addon folders (Win+R)

For a standard installation, paste these paths into Win+R. Adjust the Steam library prefix if needed.

**Source files:**

```text
C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\content\csgo_addons\anomeme_ui\panorama
```

**Compiled addon:**

```text
C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo_addons\anomeme_ui
```

The compiled logo is under `panorama\styles\custom_game\anocore\anomeme_banner.vtex_c`. Sources and compiled resources are separate; the build script manages both.

## 4. Upload Workshop content

Start CS2 Workshop Tools, select `anomeme_ui` and open Workshop Manager. Choose the existing **AnoCore UI** item → **Re-Upload → anomeme_ui → Submit**. Update its content; changing the description alone does not upload rebuilt files. Wait for upload and any required approval. Keep the existing item rather than deleting or recreating it.

The ANOMEME ID is **3815363712**, shown after `id=` in its address:
https://steamcommunity.com/sharedfiles/filedetails/?id=3815363712

Other servers should use their own published item ID. `anomeme_ui` is an addon folder name, not a Workshop ID.

## 5. Find the downloaded Workshop copy

Win+R:

```text
C:\Program Files (x86)\Steam\steamapps\workshop\content\730\3815363712
```

`730` is the CS2 app ID; `3815363712` is the addon ID. The folder exists only after Steam downloads the content there; check other Steam libraries if necessary. Do not edit this download copy for publishing.

## 6. Connect the server and test

The existing ANOMEME MultiAddonManager setup uses:

```text
mm_extra_addons "3815363712"
mm_addon_mount_download "1"
```

Preserve other addon IDs. Keep these settings in the existing server/MultiAddonManager CFG, not `core.json`. Restart server and CS2, connect and enter `!anomenu` in game chat. The dashboard needs a matching server build, not just new XML/CSS. See [quick installation](quick-install.md).

## Changes and troubleshooting

- Logo: replace the repository PNG, retain the descriptor, build and update Workshop content.
- Colors/spacing: edit `menu.css`, save, build and upload.
- Layout: edit `menu.xml`; new panel IDs also require server-plugin changes.
- Missing data: check plugin version, enabled modules, database startup and `!anostatus`.
- Old graphics: check compilation, the actual uploaded content, downloads and server/client restarts.
- Test Back separately from pagination: Back changes views; Prev/Next page change pages.

CI checks sources and server code. Valve compilation, mouse interaction and screen coverage require a real CS2 client test. Back up existing files before updating.
