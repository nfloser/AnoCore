# AnoCore development handoff

## Current workstreams

- Full functional scope remains in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Targeting/immunity #37 / PR #38 is merged in main at `ad73aa7cfea8ab4342e599cca8b65146e163551b`.
- Persistent moderation #41 / PR #42 is merged in main at `fd20c543ece4a95f8d1978a1a5a97bb40dc3f97d`.
- Moderation admin commands #43 / PR #44 are merged in main at `c7a7eff2f6db28e5c809c757e1b8e1858d69899e`.
- Active connect-ban enforcement work is #45 / PR #46 / branch `feature/45-connect-ban-enforcement`.
- Independent CustomHud/AnoVeto draft PR #40 is integrated with current foundations and passed CI #158; it still requires real CS2/DatHost Panorama acceptance.
- Extended commands #36 remain separate.

## Known-good #45 checkpoint

- Tested code head before documentation commits: `133041de8ff84b76bc608b3d39105f384e354413`.
- CI run: `35452871507` (#177).
- Release build: passed with zero warnings and zero errors.
- Test suite: 177/177 passed, including MariaDB integration.
- Formatting: passed.
- Development publish: passed.
- Deployment package validation: passed.
- Artifact upload: passed.
- Later commits only update documentation; require final exact-head CI before merge.

## Implemented in #45 / PR #46

- New engine-independent `IPlayerDisconnectAction` boundary.
- New `ConnectBanEnforcement` lifecycle subscriber in `AnoCore.Modules.Admin`.
- Connect and reconnect events query the shared `IModerationService` at the current UTC instant.
- Active `ModerationRestriction.Connect` invokes exactly one disconnect action per current session.
- Duplicate lifecycle events for the same banned session do not duplicate disconnect calls.
- A new reconnect session is evaluated independently.
- Expired/revoked/inactive connect restrictions allow the session.
- Failed disconnect attempts release their session marker so later events can retry.
- Cancellation is propagated.
- `PlayerDisconnectedEvent` releases tracked session state to avoid unbounded growth.
- Disposal unsubscribes all lifecycle handlers and clears tracked state.
- Detailed behavior is documented in `docs/connect-ban-enforcement.md`.

## Scope boundary

PR #46 deliberately contains no CounterStrikeSharp disconnect call and does not touch the plugin composition root.

The next small #17 package should implement the native `IPlayerDisconnectAction` adapter and compose `ConnectBanEnforcement` into the plugin. Keep that package limited to connect bans.

Chat gag and voice mute enforcement remain separate later packages.

## Next steps

1. Run final CI on the exact #46 documentation head, self-review and merge with expected-head SHA if green and mergeable.
2. Start a new branch from merged main for the native CounterStrikeSharp disconnect adapter/composition only.
3. Add real-server acceptance for active ban rejection/disconnect, expired/revoked allow path, reconnect and unload.
4. Then implement chat gag enforcement as a separate package, followed by voice mute enforcement.
5. Keep PR #40 draft until its real Panorama acceptance is recorded.
6. Keep #36 Extended Commands separate until the native administration foundations it consumes are merged.

## Integration rules

Reuse the single shared event bus, `IModerationService`, targeting, authorization and command services. Do not create duplicate moderation state. Native engine calls after async work must be marshalled onto the server update thread where CounterStrikeSharp requires it.

License, NOTICE and source provenance must remain intact.
