# Full-system disposable test checkpoint — 2026-10-02

- Issue #157 / branch `integration/157-full-system-test` assembles the previously independent code-complete native stacks without promoting their unverified behavior to `main`.
- The candidate currently combines playtime/combat/toplists/ranks, rank administration and notifications, formatted chat/colors/tags, settings commands/menu, connect-ban, kick, warnings, CustomHud AnoVeto with live config reload, and the packaged module SDK.
- Merge conflicts were resolved by preserving newer core/config/settings contracts while adding the feature stacks; AnoVeto keeps both Panorama CustomHud presentation and the newer atomic `anoveto`/`maps` reload registrations.
- Use `docs/full-system-test.md` and only the exact green CI artifact from the integration PR. Native CS2/DatHost observations remain release gates.

# Module API compatibility checkpoint — 2026-09-30

- Issue #153 / branch `feature/153-module-api-compat` adds an explicit AnoCore module API level to the public abstractions without changing the existing descriptor constructor.
- `ModuleHost` rejects requirements outside the supported API-level range before initialization, so incompatible modules cannot create owned resources or run startup code.
- Unit coverage protects the legacy default, explicit requirements, invalid levels and pre-initialization rejection. External module discovery, packaging/examples and the wider Web/API scope remain in #22.

# AnoVeto live configuration checkpoint — 2026-09-30

- Issue #151 / branch `feature/151-anoveto-live-config` makes the existing `anoveto` policy and `maps` catalog visible to the shared reload commands.
- Successful reloads affect the next vote only. An active vote keeps the policy and selected maps it was created with; invalid candidates retain the previous accepted runtime values.
- `Enabled` remains a restart-only switch, and registration rollback/disposal removes both reload handles. This package does not claim native CS2/DatHost behavior.

# Configuration reload command checkpoint — 2026-09-29

- Issue #148 / branch `feature/148-config-reload-commands` exposes the registry through permissioned `anoconfigs` and `anoreloadconfig <name>` core commands.
- Reload remains deliberately single-registration and atomic: unknown, invalid or failed loads produce a bounded command failure and preserve the last accepted value.
- Unit coverage includes ordering, player/console authorization, failure preservation and command cleanup. Concrete module adoption remains open; this package makes no native CS2 behavior claim.

# Configuration reload registry checkpoint — 2026-09-29

- Issue #146 / branch `feature/146-config-reload-registry` adds shared module-owned typed reload registrations.
- Candidate values become visible only after successful load and validation; failures, cancellation and disposal retain the last accepted value.
- Per-registration reloads serialize, descriptors are bounded/sorted and handles work with `context.Own(...)`. Concrete module adoption remains open.

# Versioned configuration checkpoint — 2026-09-29

- Issue #144 / branch `feature/144-versioned-config-migrations` adds an optional versioned configuration contract over the existing JSON store.
- Legacy flat JSON is version 0; exact ordered migrations validate before atomic rewrite. Future versions, gaps and failures preserve the source.
- Runtime composition exposes the capability when supported. Module metadata and complete reload remain open.

# Durable player-profile events checkpoint — 2026-09-29

- Issue #142 / branch `feature/142-player-profile-events` adds public session-scoped loaded/unloaded events after successful profile writes.
- Runtime bootstrap and later connects use the same durable loaded path; reconnect replaces the old session explicitly, while delayed stale loads cannot publish.
- Event observer failures are isolated after commit. This backend/SDK package does not claim native CS2 behavior.

# Module-owned cleanup checkpoint — 2026-09-29

- Issue #140 / branch `feature/140-module-owned-cleanup` adds `IAnoModuleContext.Own` and a fresh host-managed resource scope per load attempt.
- Owned registrations dispose LIFO after shutdown and on initialization/shutdown failure. Tests cover ordering, partial startup and combined diagnostics.
- Modules must adopt `context.Own(...)` for their catalog/event/command handles. Native behavior is unaffected.

# Player settings batch checkpoint — 2026-09-29

- Issue #138 / branch `feature/138-player-settings-batch` adds optional transactional module-data batches and homogeneous typed player-setting batches.
- Batches are validated and bounded before storage, commit in one MariaDB transaction, then emit existing value-free change events in input order. Tests cover visibility after commit, duplicates, failure and database rollback.
- This backend/SDK package does not claim native CS2 behavior. Complete module registration and draft settings UI PRs #133/#135 remain separate.

# Player settings bulk-reset checkpoint — 2026-09-28

- Issue #136 / branch `feature/136-player-settings-reset` adds an optional prefix-delete store contract and an SDK reset service/event.
- MariaDB deletes exactly one player's settings namespace in one statement. Unit and MariaDB tests cover exact player/module isolation, no-op, failure, cancellation behavior and post-commit observer isolation.
- This backend package does not claim native CS2 behavior. Settings commands/menu remain in draft PRs #133/#135; broader registration/batching and SDK work remain open.

# AnoCore development handoff

## Toggle settings catalog checkpoint — 2026-09-28

- Issue #130 / branch `feature/130-player-toggle-catalog` starts from main after the merged #128 / PR #129 settings event package. Modules register validated bool options by owner, receive stable sorted snapshots and release descriptors on unload.
- Unit tests cover validation, bounds, duplicate ownership and stale handles; the MariaDB runtime composition test checks the shared catalog. Docs: `docs/sdk-settings-events.md`. CI and PR review follow this checkpoint.
- Existing module options, player-facing menu, commands and full SDK #22 still need separate work. Native draft PRs remain gated on real CS2/DatHost tests.

## Settings change event checkpoint — 2026-09-28

- Issue #128 / branch `feature/128-player-setting-events` starts from main. Durable settings mutations publish value-free `PlayerSettingChangedEvent` after success; no-op reset and storage failures are silent.
- Runtime composition wires the shared bus. Subscriber errors are isolated after commit. Tests cover persistence, event order, no-op/failed writes and observer failure. See `docs/sdk-settings-events.md`.
- CI and PR review are pending at this documentation checkpoint. Settings catalog/UI and the rest of SDK #22 remain open.

## Warning persistence checkpoint — 2026-09-24

- Issue #70 / PR #71 adds warning contracts, runtime service, MariaDB migration 004, repository and integration tests; branch `feature/70-persistent-warnings`.
- Warning history is retained after expiry or administrative clearing. Clearing locks selected active rows in one transaction and records actor, time and reason. Expiry is exclusive; result limits are bounded.
- CI #284 validates the backend package. Warning commands, notifications and real-server acceptance are separate upcoming work; this PR does not claim end-to-end warning functionality.
- PR #67 (native connect-ban) and stacked PR #69 (kick commands) remain draft pending disposable CS2 acceptance. Do not merge those without live verification.
- Next: verify exact-head CI for #71, self-review and merge the backend; then build warning commands using the shared service and permissions. Continue remaining scope in `docs/functional-acceptance.md`.

## Current checkpoint — 2026-09-24

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
