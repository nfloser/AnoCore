# AnoCore development handoff

## Current workstreams

- Full functional scope remains in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Targeting/immunity #37 / PR #38 is merged.
- Persistent moderation #41 / PR #42 is merged.
- Persistent moderation admin commands #43 / PR #44 are merged.
- Connect-ban policy/native disconnect/bootstrap #45 through #50 are merged.
- Live moderation command composition #51 / PR #52 is merged.
- Race-safe moderation snapshots #53 / PR #54 are merged in main at `5c98f3990b328386922b2b0bafe45d9c4dc6a8d3`.
- Active communication-policy work: issue #55 / PR #56 / branch `feature/55-communication-policy`.
- Independent CustomHud/AnoVeto PR #40 remains draft until real CS2/DatHost Panorama acceptance.
- Extended commands #36 and remaining #17 native chat/voice, tags, messaging and admin-UI packages remain separate.

## Known-good #55 checkpoint

- Reviewed code/test head before documentation commits: `0b924f2961fd30bd4ce610c1f2a3c05918cf66c4`.
- CI run: `35456178405` (#213).
- Release build: passed with zero warnings and zero errors.
- Test suite: 195/195 passed, including MariaDB integration and the communication-policy expiry regression.
- Formatting: passed.
- Development plugin publish: passed.
- Deployment package validation: passed.
- Artifact upload: passed.
- Later commits only update moderation documentation, acceptance and this handoff; require final exact-head CI before merge.

## Implemented in #55 / PR #56

- Engine-independent `IModerationCommunicationPolicy`.
- Channels: Chat and Voice.
- Decisions: Allowed, Blocked and SnapshotUnavailable.
- Chat maps only to `ModerationRestriction.Chat`.
- Voice maps only to `ModerationRestriction.Voice`.
- Silence blocks both channels through the existing two-flag moderation state.
- Evaluation is synchronous and uses only `IModerationSnapshotProvider`; no database access occurs in the policy.
- Snapshot miss is explicit and never silently treated as unrestricted.
- `TimeProvider` supplies current UTC time so temporary restrictions expire locally at the exact boundary.
- Tests cover unrestricted, chat-only, voice-only, silence, cache miss, invalid channel and exact expiry behavior.

## Integration boundary

#56 intentionally does not add CounterStrikeSharp say/say_team listeners, voice hooks, snapshot warming or plugin composition.

Native consumers must:

1. warm moderation state through the shared `IModerationService` at a lifecycle boundary;
2. use the shared snapshot provider/policy synchronously in high-frequency callbacks;
3. treat `SnapshotUnavailable` as an explicit not-ready state;
4. avoid MariaDB calls from chat/voice callbacks;
5. preserve unload/hot-reload cleanup and reconnect/session safety.

## Next steps

1. Run final CI on the exact documentation/handoff head, review PR #56 and merge with expected-head SHA if green.
2. Create the next small #17 package for snapshot warming/invalidation on player lifecycle.
3. Build native chat-gag enforcement separately from native voice-mute enforcement if their CounterStrikeSharp hook lifecycles differ.
4. Keep PR #40 draft until real CS2/DatHost CustomHud acceptance is recorded.
5. Keep #36 Extended Commands separate and reuse shared authorization/targeting/moderation.
6. Preserve small test-first commits, exact-head CI, self-review and this handoff before context boundaries.

## Integration rules

Do not create duplicate moderation caches, stores or permission systems. Use `IModerationCommunicationPolicy` over the shared `IModerationSnapshotProvider` for synchronous communication decisions. Engine mutations after async work must be marshalled onto the server update thread.

License, NOTICE and source provenance must remain intact.
