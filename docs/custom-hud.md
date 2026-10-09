# Custom HUDs

AnoCore exposes reusable rich HUD contracts in `AnoCore.Abstractions.Hud`. They are designed for CS2's `custom_hud_layout` feature and remain independent of CounterStrikeSharp so gameplay modules can be tested without a running server.

## Capabilities

A `CustomHudDefinition` declares:

- a stable `CustomHudId`;
- a Panorama layout resource below `panorama/layout/custom_game/`;
- the root panel whose `shown` class controls visibility;
- optional clickable button IDs;
- optional input capture.

`ICustomHudService` provides per-player show/hide, dialog-variable text updates and CSS class updates. Interactive layouts receive validated button clicks through `CustomHudClickContext`.

This supports both kinds of UI:

- **interactive surfaces**, such as AnoVeto, where the cursor is captured and button IDs are routed back to module logic;
- **information surfaces**, such as a tournament bracket or match-status overlay, where input capture is disabled and the module only updates text/classes.

## Engine adapter

`CounterStrikeCustomHudService` is the only CounterStrikeSharp-specific implementation. It uses API 374's `CCSCustomHudLayout` entity and custom-HUD click listener. The adapter:

- creates one owned entity per registered layout;
- scopes clicks by entity handle, declared button ID and currently visible player state;
- stores desired state by SteamID rather than player slot;
- resets slot-scoped engine state when a client joins;
- recreates entities after map changes and reapplies desired visible state;
- releases input capture and removes owned entities on registration disposal or plugin unload;
- removes stale owned entities with the same layout resource after hot reload.

## AnoVeto

AnoVeto registers `panorama/layout/custom_game/anocore/ano_veto_cards.xml` with eight map button IDs plus a Close button. `!anoveto create` starts the existing shared vote session and opens the HUD for eligible players. Bare `!anoveto` reopens the active HUD. A map click is translated back into the existing `AnoVetoCoordinator` and shared `IVoteService`; the HUD layer does not own vote semantics.

After a successful ballot the HUD closes for that player. Completion, cancellation and expiry hide it for everyone and release input capture.

Map cards support genuine Workshop previews prepared as local addon textures and
selected through per-player classes. The native API does not dynamically set
Image.src. See [anoveto-map-previews.md](anoveto-map-previews.md) for cached Steam
metadata, preparation, build/delivery and neutral missing-image behavior.

## Client resources

The plugin can create and update the server entity, but the Panorama XML/CSS must be compiled with CS2 Workshop Tools and delivered to clients. Source assets live under `ui/AnoCore`; `ui/AnoCore/build.ps1` compiles the current AnoVeto layout into the `anomeme_ui` addon by default.

CI validates IDs and source structure. A real CS2 client remains the acceptance gate for rendering, click delivery and cursor behavior.

## Shared player menu

`!anomenu` opens a home page over the enabled Statistics, Ranks, Progression,
Challenges, Achievements, AnoRating, Settings, Chat tags and Tournament status
commands. The same logical menu definitions drive both presenters. Existing direct
menu commands therefore continue to work; no second statistics/settings store exists.
Only registered destinations appear. Protected destinations are filtered at opening,
and execution goes through the command registry to recheck current permissions.
Administration currently exposes an authorized **command reference**, not mutation
forms: targeted moderation and server actions still use their documented commands.

The shared `menu.xml` / `menu.css` displays six clickable rows per page, Home,
Previous, Next and Close. Each player's page, labels and cursor capture are independent.
Information rows remain read-only. Dynamic labels use plain text (`html="false"`)
and are bounded; existing HTML-escaped menu labels are decoded for Panorama.
Selection uses the exact logical definition and captured player session; pending
selections cannot reopen a closed/reconnected presentation. Disconnect, map end,
registration rollback and unload clean up owned state and input capture.

### Enable after automatic addon delivery is configured

1. Build **both** layouts with `ui/AnoCore/build.ps1` on a Windows CS2 Workshop
   Tools installation. CI only validates source structure; it does not compile Valve resources.
2. Publish the compiled `anomeme_ui` addon using Workshop Tools. The published
   item must contain `panorama/layout/custom_game/anocore/menu.vxml_c` and
   `panorama/styles/custom_game/anocore/menu.vcss_c`, in addition to AnoVeto resources.
3. Configure the server's addon distribution (for example MultiAddonManager) with
   the **actual published Workshop ID**. Verify that a clean client automatically
   downloads and mounts it on join. Do not require manual client file copies.
4. Add `"PanoramaMenusEnabled": true` to the existing `config/core.json`, preserving
   the database connection and all other properties, then restart the server.
5. Test `!anomenu`, all five direct menu commands, paging, changes, Home and Close.
   Repeat with two humans showing different pages; disconnect/rejoin and change
   map while a menu is open. Verify that cursor capture is released after each exit.

The flag defaults to false, keeping CenterHTML usable until resources are distributed.
The server entity API cannot prove that a particular client mounted the XML/CSS;
there is no reliable automatic missing-resource fallback. If the addon is missing,
set the flag back to false and restart. No Workshop ID is fabricated or configured
by this source change. `-InstallLocalClient` remains a developer-only smoke-test
option, not the deployment path for players.
