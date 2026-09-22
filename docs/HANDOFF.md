# AnoCore development handoff

## Current workstreams

- Full functional scope remains in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Targeting/immunity #37/#38, persistent moderation #41/#42, moderation commands #43/#44, connect-ban lifecycle/native enforcement #45-#50, live command composition #51/#52, moderation snapshots #53/#54, synchronous communication policy #55/#56 and lifecycle warming #57/#58 are merged.
- Active native chat-gag enforcement: issue #59 / PR #60 / branch `feature/59-native-chat-gag`.
- Independent CustomHud/AnoVeto PR #40 remains draft until real CS2/DatHost Panorama acceptance.
- Extended commands #36 and remaining #17 voice, tags, messaging and admin-UI packages remain separate.

## Known-good #59 checkpoint

- Reviewed implementation head before this documentation checkpoint: `0895f2958b7bf82e74eb3fcc4d3526fb9fd89729`.
- CI run: `35459178761` (#239).
- Release build: passed with zero warnings and zero errors.
- Test suite: 210/210 passed, including MariaDB integration and moderation lifecycle/chat tests.
- Formatting: passed.
- Development plugin publish: passed.
- Deployment package validation: passed.
- Artifact upload: passed.
- After this documentation update, require one final exact-head CI run before merge.

## Implemented in #59 / PR #60

- Engine-independent `ModerationChatGate` with fail-closed behavior.
- `ModerationCommunicationRuntime` composes the merged snapshot lifecycle, synchronous communication policy and chat gate.
- Existing online players are warmed explicitly during live plugin activation.
- Native CounterStrikeSharp pre-command listeners are registered for `say` and `say_team`.
- Allowed cached state returns `HookResult.Continue`.
- Active Chat restriction returns `HookResult.Handled`.
- Snapshot unavailable/not-ready also returns `HookResult.Handled`.
- Server/invalid/bot/HLTV callers are not mapped to a false moderation identity.
- Valid human players without a SteamID yet are blocked fail-closed.
- Chat listener performs no MariaDB read on the hot path.
- Applying/revoking through shared moderation state updates loaded snapshots so gag/ungag changes can take effect without reconnect.
- Listener registration rolls back if the second native listener fails to register.
- Dispose removes both listeners idempotently.
- Plugin activation failure and unload dispose native listener + communication lifecycle cleanly.
- Native behavior and target-server verification checklist are documented in `docs/chat-moderation.md`.

## Review status

Reviewed:
- fail-closed decision mapping,
- no-DB hot path,
- online bootstrap,
- reconnect/disconnect snapshot lifecycle reuse,
- invalid/non-human caller behavior,
- listener registration rollback,
- unload and activation-failure cleanup,
- command listener ownership and disposal ordering.

No code-review blocker is currently known. Real CS2 command/listener ordering and actual chat suppression still require disposable-server acceptance.

## Scope boundary

#60 does not implement voice mute enforcement, chat/tag formatting, custom message rewriting or admin HUD. Those remain separate #17 packages.

## Next steps

1. Run final exact-head CI after this documentation checkpoint; if green, update PR #60 metadata, self-review and merge with expected-head SHA.
2. Implement native voice-mute enforcement as the next small #17 package, reusing the same moderation snapshot/policy layer.
3. Keep PR #40 draft until real CustomHud acceptance is recorded.
4. Keep #36 Extended Commands separate and reuse shared targeting/authorization/moderation foundations.
5. Preserve small test-first commits, exact-head CI and this handoff before context boundaries.

## Integration rules

Do not create duplicate moderation caches, stores or permission systems. High-frequency native adapters must evaluate the shared synchronous snapshot policy and must not query MariaDB directly. Engine mutations after asynchronous work must return to the server update thread where required.

License, NOTICE and source provenance must remain intact.
