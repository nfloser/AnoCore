## Atomic rank administration checkpoint — 2026-09-26

- Issue #100 / draft PR to follow / branch `feature/100-rank-adjustment-admin-service` stacks on #99. A shared runtime service applies give/take/set/reset adjustments with row locking and writes the matching action audit inside the same MariaDB transaction.
- MariaDB tests cover the operation sequence, restart-visible state, actor/reason metadata, bounds and rollback when audit insertion fails. CI #360 passed build, 276 tests including MariaDB, formatting, publish and package validation on reviewed code head `718732221759be93ad0d950784c41ed6a54be7a1`. Review checked row-locking, missing-row seeding, bounded arithmetic, reset idempotency and transactional audit rollback; no known code-review blocker remains.
- No commands, permissions, target resolution or native notifications are claimed in this package; those remain the next rank-administration step.

## Applied rank adjustments checkpoint — 2026-09-26

- Issue #98 / draft PR to follow / branch `feature/98-apply-rank-adjustments` stacks on #97. Rank view and top-list queries combine combat score and durable adjustment in one snapshot, floor at zero and include adjustment-only offline SteamIDs.
- MariaDB coverage checks positive/negative adjustments, zero flooring, deterministic ties, own placement and offline-only entries. CI #356 passed build, 273 tests including MariaDB, formatting, publish and package validation on reviewed code head `198998861d13b53b73ca010567e25d8d8c256968`. Review fixed incomplete migration setup in existing score tests; no known code-review blocker remains.
- This package does not add give/take/set/reset commands, permissions, audit, tags, menus or native notifications. Keep draft pending the combat/live-acceptance chain.

## Rank adjustment persistence checkpoint — 2026-09-26

- Issue #96 / draft PR #97 / branch `feature/96-rank-adjustments` stacks on #95. Migration 007 and `IRankAdjustmentRepository` persist bounded per-player point adjustments with actor/time metadata, overwrite and idempotent reset, including offline SteamIDs.
- CI #353 passed build, 272 tests including MariaDB, formatting, publish and package validation on reviewed code head `018c33f2ba4c96db1f2a6b111378879f46028a46`. Review removed a player-table foreign key that broke established database cleanup and unnecessarily prevented offline adjustments.
- This is backend only: score queries and give/take/set/reset commands do not consume adjustments yet. Keep #97 draft pending the native combat dependency and later end-to-end administration acceptance.

## Rank transition checkpoint — 2026-09-26

- Issue #94 / draft PR to follow / branch `feature/94-rank-transitions` stacks on #93. Adds engine-independent promotion/demotion evaluation across exact and multi-rank threshold changes.
- This is shared policy only, not native player notification. CI #347 passed build, tests, formatting, publish and package validation on reviewed code head `9c8a1f4a9ad84cc8dffaaa468de6d30e67cae22f`; final documentation-only head CI remains.

## Own rank placement checkpoint — 2026-09-26

- Issue #92 / draft PR to follow / branch `feature/92-rank-placement` stacks on #91. `anorank` shows deterministic placement using the same weighted score and SteamID64 tie order as `anotopranks`; players without combat rows are unranked.
- Initial CI #343 passed. Review removed a two-query consistency window: `anorank` now derives points, rank and placement from one score-placement snapshot. Final exact-head CI remains.
- MariaDB ties and missing players are covered. Keep draft pending #79 native acceptance.

## Rank progress checkpoint — 2026-09-26

- Issue #90 / draft PR to follow / branch `feature/90-rank-progress` stacks on #89. `anorank` reports exact points remaining to the next validated threshold or that the highest configured rank has been reached.
- CI #341 passed build, tests, formatting, publish and package validation on reviewed code head `0e63d40220cadb3247f1dfc70d3075d6ba04479a`; final documentation-only head CI remains to verify.
- Review checked exact thresholds, the highest rank and negative input. Keep draft pending #79 native combat acceptance; this is not rank administration or automatic promotion notification.

## Rank leaderboard checkpoint — 2026-09-25

- Issue #88 / draft PR to follow / branch `feature/88-rank-leaderboard` stacks on #87. Adds a bounded score query using the current rank weights and `anotopranks [page]`, with stable SteamID64 tie ordering and no duplicate score store.
- CI #339 passed build, unit/MariaDB tests, formatting, publish and package validation on reviewed code head `c552e4ed031c6f03af22a9c5fe00cdafb1ccf19e`. Final documentation-only head CI remains to verify.
- Review added command-registration rollback coverage. Keep draft pending #79 native combat acceptance; rank administration, menus, tags and notifications remain open.

## Combat rank checkpoint — 2026-09-25

- Issue #86 / draft PR to follow / branch `feature/86-combat-rank` stacks on #85. Adds `config/ranks.json`, validated combat-derived scoring and `anorank` with plugin lifecycle integration. See `docs/ranks.md`; exact-head CI and real-server acceptance remain to verify.
- Point weights rescore historical combat events. No rank overrides, menus, tags, notifications or toplist yet. Keep the PR draft pending the combat ingestion live gate.

## Combat top lists checkpoint — 2026-09-25

- Issue #84 / draft PR to follow / branch `feature/84-combat-leaderboards` stacks on #81. Adds persisted death/assist top lists with deterministic ties, bounds, unit and MariaDB tests; CI and native acceptance remain to verify.
- Separate main fix #82/PR #83 merged: generic command failure text and argument-safe dispatch logging. This stack was branched before that merge and may need an integration update later.

## Kill leaderboard checkpoint — 2026-09-25

- Issue #80 / draft PR to follow / branch `feature/80-kill-leaderboard` is stacked on #79. Adds deterministic bounded MariaDB kill ranking and `anotopkills [page]`, with unit and integration tests. Verify CI on the final head; keep draft pending native dependency acceptance.

## Combat counters checkpoint — 2026-09-25

- Issue #78 / draft PR #79 / branch `feature/78-combat-counters`, stacked on #77: migration 006, idempotent death-event ledger, own `anokda` command and native death hook.
- MariaDB and contract tests cover replay/restart, conflicting event IDs, suicide, world death and teamkill. Verify exact-head CI after the final documentation commit.
- Native map/tick identity and player mapping need disposable-server acceptance; the user handles CS2/DatHost tests. Keep #79 draft, along with #75/#77 and other native drafts. See `docs/combat-stats.md` for the acceptance steps.
- Remaining #18 scope includes detailed stats, policies, ranks, menus and toplists; do not claim complete parity.

## Time leaderboard checkpoint — 2026-09-25

- Issue #76 / draft PR #77 / branch `feature/76-time-toplist`, stacked on playtime PR #75: bounded `anotoptime [page]` command and deterministic MariaDB total-time ranking with SteamID64 tie ordering.
- CI #313 passed build, 249 tests including MariaDB tie/pagination/restart tests, formatting, publish and package checks on the code/documentation head before this handoff edit. Verify exact head again.
- Time entries show saved profile names when available and SteamID64 for identity/fallback; rank/stat toplists, UI navigation and placement tags remain #18 work. User handles live CS2 testing; both PRs remain draft pending it.

## Session playtime checkpoint — 2026-09-25

- Issue #74 / draft PR #75 / branch `feature/74-session-playtime` adds a separate Stats module, migration 005, idempotent MariaDB session ledger, UTC-day totals and the own-playtime command.
- Connect/reconnect/disconnect events plus five-second checkpoints are integrated in the plugin. Stats composition errors are isolated from the core runtime. Unload schedules a final checkpoint; abrupt server crashes may lose time since the last successful heartbeat.
- CI #310 passed 247 tests, formatting, publish and package validation before the final composition-error correction. Check CI on the final head.
- User will perform real CS2/DatHost acceptance. PR #67 connect-ban, #69 kick, #73 warnings and #75 playtime remain draft pending observed engine behavior; #40 CustomHud/AnoVeto remains draft as well.
- #18 ranks, complete statistics, team/alive playtime and toplists, plus other acceptance rows remain open. Do not claim feature parity or a production release.

# AnoCore development handoff

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
