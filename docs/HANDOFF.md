# AnoCore development handoff

## Current workstreams

- Full functional scope remains in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Targeting/immunity #37 / PR #38 is merged.
- Persistent moderation #41 / PR #42 is merged.
- Persistent moderation admin commands #43 / PR #44 are merged.
- Connect-ban lifecycle policy #45 / PR #46 is merged.
- Native CounterStrike disconnect adapter #47 / PR #48 is merged.
- Existing-session connect-ban bootstrap #49 / PR #50 is merged.
- Active live-command composition: issue #51 / PR #52 / branch `feature/51-compose-moderation-commands`.
- Independent CustomHud/AnoVeto PR #40 passed CI #158 after integrating current foundations but remains draft until real CS2/DatHost Panorama acceptance.
- Extended commands #36 and the remaining #17 communication/admin/UI packages remain separate.

## Known-good #51 checkpoint

- Reviewed code head before this handoff commit: `c84435fa29b038db7f005e6ed0b0978ba6f3b48a`.
- CI run: `35454654365` (#193).
- Release build: passed with zero warnings and zero errors.
- Test suite: 179/179 passed, including MariaDB integration.
- Formatting: passed.
- Development plugin publish: passed.
- Deployment package validation: passed.
- Artifact upload: passed.

## Implemented in #51 / PR #52

- Live plugin composition creates `ModerationTargetGateway` from the shared:
  - player registry,
  - target resolver,
  - target authorization service,
  - authorization service.
- It creates `ModerationCommandExecutor` from that gateway and the shared moderation service.
- It creates `ModerationCommandController` before the native CounterStrike command bridge enumerates command descriptors.
- The existing `CounterStrikeCommandBridge` therefore exposes all eight already-tested moderation commands without a second command implementation.
- The controller is retained for the active runtime lifetime.
- Unload removes native command bindings first, then unregisters the moderation command descriptors.
- Activation rollback disposes the command bridge, moderation controller, AnoVeto runtime and shared runtime consistently.
- Review fix: controller construction was moved inside the activation try/catch so a registration collision cannot escape the rollback path and orphan the runtime.

## Live moderation command surface

- `!anoban <target> <minutes> [reason]`
- `!anounban <target> [reason]`
- `!anomute <target> <minutes> [reason]`
- `!anounmute <target> [reason]`
- `!anogag <target> <minutes> [reason]`
- `!anoungag <target> [reason]`
- `!anosilence <target> <minutes> [reason]`
- `!anounsilence <target> [reason]`

The commands reuse centralized target resolution, permissions, immunity, persistence and audit history. They do not implement their own player or moderation storage.

## Scope boundary

#51 only makes the persistent commands reachable through the live CounterStrike command bridge.

Already merged connect-ban policy/native disconnect code is separate. Remaining #17 native work still includes:

- voice-mute enforcement,
- chat-gag enforcement,
- live refresh when communication moderation changes,
- admin UI/audit presentation,
- chat/name/clan tags and shared messaging surfaces.

## Next steps

1. Run final exact-head CI after this handoff commit, self-review PR #52 and merge with expected-head SHA if green and mergeable.
2. Continue #17 in another small branch with communication restriction propagation/enforcement rather than combining voice, chat, tags and UI in one change.
3. Keep PR #40 draft until real CS2/DatHost CustomHud acceptance is recorded.
4. Keep #36 Extended Commands separate and consume the shared target/authorization/moderation foundations.
5. Preserve small commits, CI gates and handoff notes before context boundaries.

## Integration rules

Use the shared services in `RuntimeServices`. Do not create duplicate player registries, target resolvers, authorization/immunity logic, command registries or moderation stores. Engine work after asynchronous operations must be marshalled onto the server update thread. Preserve cleanup on unload, hot reload and activation rollback.

License, NOTICE and source provenance must remain intact.
