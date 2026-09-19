# AnoCore development handoff

## Current workstreams

- Full functional scope remains in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Targeting/immunity #37 / PR #38 is merged in main at `ad73aa7cfea8ab4342e599cca8b65146e163551b`.
- Persistent moderation #41 / PR #42 is merged in main at `fd20c543ece4a95f8d1978a1a5a97bb40dc3f97d`.
- Moderation admin commands #43 / PR #44 are merged in main at `c7a7eff2f6db28e5c809c757e1b8e1858d69899e`.
- Connect-ban lifecycle policy #45 / PR #46 is merged in main at `ab0eca64d1dc9d69365d260c68758554fba95c9b`.
- Active native disconnect-adapter work is #47 / PR #48 / branch `feature/47-native-disconnect-adapter`.
- Independent CustomHud/AnoVeto draft PR #40 is integrated with the shared foundations and passed CI #158; it still requires real CS2/DatHost Panorama acceptance.
- Extended commands #36 remain separate.

## Known-good #47 checkpoint

- Initial native adapter head `d10affdc47e31c4de097e9f7f6d0838058104e1a` passed CI #183.
- The adapter then received one review hardening commit: queued disconnect callbacks now no-op if their cancellation token was cancelled before the server update executes.
- Final documentation commits follow that code change; require final exact-head CI before merge.
- Existing suite remains 177 tests before any later test additions.
- CounterStrikeSharp package remains pinned at API 1.0.374.

## Implemented in #47 / PR #48

- New `CounterStrikePlayerDisconnectAction` in the plugin layer.
- `AnoCore.Plugin` now references `AnoCore.Modules.Admin` for the shared disconnect-action contract.
- Native work is marshalled through `Server.NextWorldUpdate`.
- Before disconnecting, the callback checks:
  - operation cancellation;
  - the shared registry still contains the player;
  - the player is still connected;
  - the current `PlayerSessionId` exactly matches the originally banned session.
- Live controller resolution uses SteamID64 and excludes invalid/bot/HLTV controllers.
- Current sessions are disconnected through CounterStrikeSharp API 374:
  `CCSPlayerController.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED)`.
- The free-form AnoCore moderation reason remains in persistent audit/history; CounterStrikeSharp's disconnect method accepts a network reason enum rather than that text.
- Detailed behavior is recorded in `docs/connect-ban-enforcement.md`.

## Scope boundary

PR #48 deliberately does not edit `AnoCorePlugin.cs`, because draft PR #40 owns that file in parallel.

Therefore the native adapter exists and compiles/packages, but `ConnectBanEnforcement` is not yet composed into the live plugin.

The next integration package should reconcile current main with #40 and wire only:

- the shared runtime/event/player services;
- `CounterStrikePlayerDisconnectAction`;
- `ConnectBanEnforcement`;
- disposal during unload/startup rollback.

Chat gag and voice mute enforcement remain later independent packages.

## Next steps

1. Run final CI on the exact #48 documentation head, self-review and merge with expected-head SHA.
2. Reconcile PR #40 with the latest main without losing its CustomHud changes.
3. Add the connect-ban composition in one small integration package/commit set.
4. Run real-server acceptance: active ban disconnect, expired/revoked allow, stale/reconnect safety and unload.
5. Then implement chat gag enforcement as a separate package, followed by voice mute enforcement.
6. Keep #36 Extended Commands separate until these administration foundations are integrated.

## Integration rules

Reuse the shared player registry, event bus and `IModerationService`. Never resolve a queued disconnect solely by SteamID without rechecking the original session token. Native engine calls remain on the server update thread.

License, NOTICE and source provenance must remain intact.
