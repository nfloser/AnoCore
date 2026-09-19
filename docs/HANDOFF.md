# AnoCore development handoff

## Current workstreams

- Full functional scope remains in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Targeting/immunity #37/#38, persistent moderation #41/#42, moderation commands #43/#44, connect-ban lifecycle/native enforcement #45-#50, live command composition #51/#52, moderation snapshots #53/#54 and synchronous communication policy #55/#56 are merged.
- Communication policy merge commit on main: `27672fe5bf28ef0a311ac0c96f36e66919df1654`.
- Active snapshot-lifecycle work: issue #57 / PR #58 / branch `feature/57-moderation-lifecycle-warming`.
- Independent CustomHud/AnoVeto PR #40 remains draft until real CS2/DatHost Panorama acceptance.
- Extended commands #36 and remaining #17 native chat/voice, tags, messaging and admin-UI packages remain separate.

## Known-good #57 checkpoint

- Reviewed implementation/test head before documentation-only commits: `8fa4ea47b2eb9e1f66f427a331d931b6f1dc49a6`.
- CI run: `35456745727` (#221).
- Release build: passed with zero warnings and zero errors.
- Test suite: 203/203 passed, including MariaDB integration and lifecycle race/failure tests.
- Formatting: passed.
- Development plugin publish: passed.
- Deployment package validation: passed.
- Artifact upload: passed.
- Later commits only update moderation docs, acceptance and this handoff; require final exact-head CI before merge.

## Implemented in #57 / PR #58

- Engine-independent `ModerationSnapshotLifecycle`.
- Subscribes to `PlayerConnectedEvent`, `PlayerReconnectedEvent` and `PlayerDisconnectedEvent`.
- Connect warms moderation through shared `IModerationService.GetStateAsync`.
- Reconnect invalidates previous cache state then reloads the current player state.
- Current session is tracked per SteamID so stale disconnect events cannot invalidate a newer reconnect.
- Matching disconnect invalidates once and forgets the tracked session.
- Disconnect during a real in-flight `ModerationService` warm ends with no cached snapshot.
- Warm failures do not create an available snapshot.
- Dispose unsubscribes events and cancels in-flight warm work without surfacing an unload failure.
- Late warm completion after disconnect/dispose is cleaned up without erasing a separately tracked newer session.
- No second moderation cache/store is introduced.

## Scope boundary

#58 intentionally does not register CounterStrikeSharp listeners and does not block native chat or voice itself. It prepares the shared cache lifecycle so later high-frequency adapters can use `IModerationCommunicationPolicy` synchronously without MariaDB reads.

## Next steps

1. Run final exact-head CI after documentation, self-review PR #58 and merge with expected-head SHA if green.
2. Implement native chat-gag interception as a separate small #17 package.
3. Implement native voice-mute enforcement separately if its CounterStrikeSharp lifecycle/API differs.
4. Compose those adapters with the merged snapshot lifecycle and synchronous communication policy.
5. Keep PR #40 draft until real CS2/DatHost CustomHud acceptance is recorded.
6. Keep #36 Extended Commands separate and reuse shared authorization/targeting/moderation.
7. Preserve small test-first commits, exact-head CI and this handoff before context boundaries.

## Integration rules

Do not create duplicate moderation caches, stores or permission systems. Warm asynchronously at player lifecycle boundaries, then evaluate communication synchronously through the shared policy. Treat snapshot miss as not-ready, not unrestricted. Engine mutations after async work must return to the server update thread where required.

License, NOTICE and source provenance must remain intact.
