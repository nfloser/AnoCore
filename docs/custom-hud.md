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

AnoVeto registers `panorama/layout/custom_game/anocore/ano_veto.xml` with eight map button IDs plus a Close button. `!anoveto create` starts the existing shared vote session and opens the HUD for eligible players. Bare `!anoveto` reopens the active HUD. A map click is translated back into the existing `AnoVetoCoordinator` and shared `IVoteService`; the HUD layer does not own vote semantics.

After a successful ballot the HUD closes for that player. Completion, cancellation and expiry hide it for everyone and release input capture.

## Client resources

The plugin can create and update the server entity, but the Panorama XML/CSS must be compiled with CS2 Workshop Tools and delivered to clients. Source assets live under `ui/AnoCore`; `ui/AnoCore/build.ps1` compiles the current AnoVeto layout into the `anomeme_ui` addon by default.

CI validates IDs and source structure. A real CS2 client remains the acceptance gate for rendering, click delivery and cursor behavior.
