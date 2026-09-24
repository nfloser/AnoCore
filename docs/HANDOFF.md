# AnoCore development handoff

## Current checkpoint — 2026-09-24

- PR #64 merged as `105e88464d0e16f537021d26f0e6ed8db64be053`; generic audit persistence is now on `main`.
- Issue #66 / draft PR #67, branch `fix/66-compose-connect-ban`: compose existing enforcement on activation, bootstrap existing players, dispose on rollback/unload and cancel scheduled native disconnect after unload. Code head `56b2eb85d88184c119c03b7de6a3a7d10a8eef63` before this handoff update. CI and actual CS2 session acceptance remain pending. Do not merge #67 until server behavior is verified.


- Issue #63 / PR #64, branch `feature/63-admin-audit`: generic action audit contracts, service, MariaDB migration/repository, shared runtime registration, unit/integration tests and persistence documentation are implemented.
- CI #268 passed build, tests, formatting, publish and package validation on `7b882e26711fd23c011bee322f75849e1ee91b1f`. Check exact-head CI after documentation updates before merging.
- Self-review: repository queries select the newest bounded entries; the service returns selected entries ordered by UTC time and ID. SQL parameters and restart persistence are covered by MariaDB tests.
- Next: merge #64 when exact-head CI passes; implement consuming kick/warning commands as separate #17 packages. Other roadmap items and real CS2 acceptance remain open. AnoCore is not production complete.

## Current workstreams

- Full functional scope remains in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Targeting/immunity #37/#38, persistent moderation #41/#42, moderation commands #43/#44, connect-ban enforcement #45-#50, command composition #51/#52, moderation snapshots #53/#54, synchronous communication policy #55/#56, lifecycle warming #57/#58 and native chat gag #59/#60 are merged.
- Active native voice-mute enforcement: issue #61 / PR #62 / branch `feature/61-native-voice-mute`.
- Independent CustomHud/AnoVeto PR #40 remains draft until real CS2/DatHost Panorama acceptance.
- Extended commands #36 and remaining #17 kick/warnings, tags, messaging and admin-UI packages remain separate.

## Known-good #61 checkpoint

- Reviewed code head before documentation commits: `0dea275f7bbab9aa188eebe4abe89bdc2d516a25`.
- CI run: `35749688740` (#253).
- Release build: passed with zero warnings and zero errors.
- Test suite: 221/221 passed, including MariaDB integration plus voice gate/coordinator ownership tests.
- Formatting: passed.
- Development plugin publish: passed.
- Deployment package validation: passed.
- Artifact upload: passed.
- Documentation-only commits follow this checkpoint; require final exact-head CI before merge.

## Implemented in #61 / PR #62

- Fail-closed `ModerationVoiceGate` reusing the existing synchronous communication policy.
- Chat and voice gates share the same warmed moderation snapshot layer.
- `ModerationVoiceCoordinator` enforces sender mutes through listener-specific overrides.
- Administrative mute intentionally uses listen overrides instead of `VoiceFlags.Muted`, so a muted sender can still hear other players.
- Existing `Default` / `Hear` overrides are remembered and restored after unmute.
- Override ownership is keyed by listener and sender session IDs, preventing stale reconnect/slot state from leaking.
- New listeners are reconciled while a sender remains muted.
- AnoCore restores only overrides it still appears to own; later external override changes are not overwritten on release/unload.
- `CounterStrikeVoiceModerationTransport` maps `Default/Mute/Hear` to CounterStrikeSharp and rejects stale/non-human/invalid sessions.
- Live host performs a fail-closed initial reconcile and then repeats every 250 ms.
- Unload and activation failure stop the timer and restore still-owned current-session overrides.
- No database access occurs inside voice reconciliation; it reads the shared moderation snapshot policy.
- Detailed behavior and real-server acceptance are documented in `docs/voice-moderation.md`.

## Review status

Reviewed fail-closed voice decisions, listen-override semantics, preservation of existing/external overrides, reconnect/session safety, new-listener behavior, controller resolution, timer lifecycle, unload and activation-failure restoration.

Review finding fixed: unmute/unload no longer overwrites an override changed by another owner after AnoCore applied its mute.

No known code-review blocker remains. Actual CS2 voice routing still requires disposable-server acceptance.

## Scope boundary

#62 does not implement kick/warnings, chat/tag formatting, custom chat rewriting or admin HUD. Those remain separate #17 packages.

## Next steps

1. Run final exact-head CI after these documentation commits; if green, update PR #62 metadata, self-review and merge with expected-head SHA.
2. Continue #17 in a fresh small branch: kick/silent-kick and warnings should reuse the merged target/authorization/moderation foundations.
3. Keep PR #40 draft until real CustomHud acceptance is recorded.
4. Keep #36 Extended Commands separate and reuse shared administration foundations.
5. Preserve small test-first commits, exact-head CI and this handoff before context boundaries.

## Integration rules

Do not create duplicate moderation caches, stores, player registries or permission systems. High-frequency communication enforcement must use shared warmed snapshots. Voice overrides must be session-safe and ownership-aware. Engine work must stay on the server thread where required.

License, NOTICE and source provenance must remain intact.
