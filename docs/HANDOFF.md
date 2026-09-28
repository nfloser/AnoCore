## Selectable chat tags checkpoint — 2026-09-28

- Issue #124 / draft PR pending / branch `feature/124-selectable-chat-tags` stacks on #123. Server-configured `ano.*` permissions gate the `anotags`, `anosettag` and `anocleartag` choices; player settings persist selection and the priority-100 provider falls back to rank when access is lost.
- Changes and authorization reload refresh current prepared chat snapshots. CS2/DatHost display, native command dispatch and reload/unload acceptance remain unverified.

## Prioritized chat-tag ownership checkpoint — 2026-09-28

- Issue #122 / draft PR #123 / branch `feature/122-chat-tag-priority` stacks on #121. The shared `chat.tag` placeholder has deterministic provider priorities: `null` falls back, while an empty value explicitly suppresses lower tags.
- The rank module supplies priority 0 and retains `rank.tag` compatibility. Existing chat configurations using `{rank.tag}` migrate through the shared slot during formatting; unload/disposal reveals the next owner safely.
- Native display and module-specific permission/custom tag providers remain separate work and require CS2/DatHost acceptance.

## Validated native chat colors checkpoint — 2026-09-28

- Issue #120 / draft PR pending / branch `feature/120-chat-colors` stacks on #119. Chat configuration now supports allow-listed named colors or sender-team color independently for the rank tag, player name and message; existing defaults remain uncolored.
- Codes are inserted only around trusted template slots and reset after each slot. Control characters in player names/messages are still replaced, so untrusted input cannot inject colors. Unknown names fail configuration validation.
- The native code table attribution is recorded in NOTICE.md. Actual CS2 color rendering remains a DatHost acceptance gate.

## Rank chat snapshot refresh checkpoint — 2026-09-28

- Issue #118 / draft PR pending / branch `feature/118-refresh-rank-chat-snapshots` stacks on #117. Every persisted combat event publishes best-effort refreshes for distinct affected players, and every committed rank give/take/set/reset refreshes the target even when notification options are disabled or the player stays within one rank.
- The refresh resolves placeholders asynchronously outside the synchronous chat hook and publishes only for the still-current connected session. Refresh failures cannot change durable combat/admin outcomes. Tests cover disabled notifications, duplicate affected players, failure isolation and explicit current-session refresh. The lifecycle retains the prior valid snapshot during refresh, and a monotonic generation prevents older same-session work from overwriting a newer result.
- Native chat/rank display remains a draft stack pending CS2/DatHost acceptance.

## Native formatted chat routing checkpoint — 2026-09-28

- Issue #116 / draft PR pending / branch `feature/116-native-chat-formatting` stacks on #115. A pure synchronous router applies command pass-through, moderation, current-session formatting and deterministic public/team recipient selection; the CounterStrikeSharp pre-listener suppresses the original line and prints the formatted result only to those recipients.
- Missing/stale warmed format state fails closed, while a formatter that did not compose leaves allowed native chat unchanged. The hot path performs no placeholder or database work. Tests cover public/team routing, command preservation, moderation ordering, missing snapshots and disabled formatting.
- The package remains native and must stay draft until the user verifies `say` argument parsing, listener ordering, team visibility, color rendering and unload/reload behavior on CS2/DatHost.

## Warmed chat format snapshot checkpoint — 2026-09-27

- Issue #114 / draft PR #115 / branch `feature/114-chat-format-snapshots` stacks on #113. Prepared public/team formats resolve asynchronous placeholders once per current player session; synchronous `TryFormat` only validates SteamID/session and substitutes a bounded sanitized message.
- Connect/reconnect, name updates, disconnect, already-online bootstrap, stale in-flight warms and unload are session-safe. Bootstrap failures are isolated. Plugin unload disposes the snapshot lifecycle before rank placeholder providers. CI #388 passed build, 314 tests including MariaDB, formatting, publish and package validation on reviewed code head `2a5ba68c6481fd35db9754981cf3d19d49672d62`.
- Review added explicit cancellation coverage and preserved the blank-name SteamID fallback; no known code blocker remains. This package does not intercept or broadcast native CS2 chat. That adapter and CS2/DatHost acceptance remain open; verify the final documentation-only head.

## Shared chat formatting checkpoint — 2026-09-27

- Issue #112 / draft PR #113 / branch `feature/112-chat-formatting` stacks on #111. A validated engine-independent formatter loads separate public/team templates, resolves shared placeholders with player context, then safely substitutes bounded player name and message data.
- Required data tokens occur exactly once; templates and untrusted fields reject/replace control characters and enforce bounds. Tests cover rank-tag composition, routing, literal user braces, sanitization, cancellation and invalid configuration. Plugin startup creates/validates the formatter and isolates configuration failure. CI #384 passed build, 308 tests including MariaDB, formatting, publish and package validation on reviewed code head `8d9919a2eae493fef37e30bab60fd15e36030282`.
- Review found and fixed missing plugin composition; no known code blocker remains. This is formatting policy, not native chat interception. The existing native hook is synchronous; a later adapter needs a session-safe warmed snapshot and CS2/DatHost acceptance. Verify the final documentation-only head.

## Shared rank tag checkpoint — 2026-09-27

- Issue #110 / draft PR #111 / branch `feature/110-rank-tags` stacks on #109. The Ranks module registers `{rank.tag}`, `{rank.name}` and `{rank.points}` in the existing shared asynchronous placeholder registry, backed by the same adjusted score-placement query and configured thresholds.
- Tags are configurable per threshold, printable, bounded to 24 characters and may be empty. Creation rolls back partial placeholder and command registration; unload removes owned registrations. Tests cover exact threshold values, query weights, missing context, invalid tags, collision rollback and disposal. CI #380 passed build, 302 tests including MariaDB, formatting, publish and package validation on reviewed code head `1153dd79bc08d07b95f5a4c3654ce60b2e97cbb2`.
- Review found and corrected import formatting; no code blocker remains. This package provides the common data source only. Native chat/clan formatting and CS2/DatHost acceptance remain open; verify the final documentation-only head.

## Rank menu checkpoint — 2026-09-27

- Issue #108 / draft PR #109 / branch `feature/108-rank-menu` stacks on #107. `anoranks [page]` opens a per-player shared menu with own adjusted rank/points/placement/progress, five deterministic leaderboard entries and bounded previous/next navigation.
- The module reuses `IMenuService`, existing score queries and the native presenter callback; it keeps no second score store. Tests cover data/labels, page bounds, navigation, console rejection, registration rollback, control-character sanitization and unload cleanup. CI #377 passed build, 297 tests including MariaDB, formatting, publish and package validation on reviewed code head `31f6a7539634cc14109949414a0e769ba5a82474`.
- Review added a second lifecycle/session gate so an in-flight query cannot register a menu after unload or disconnect; no known code blocker remains. Native CenterHtml rendering requires the user's CS2/DatHost acceptance. Rank tags remain open.

## Administrative rank notification checkpoint — 2026-09-27

- Issue #106 / draft PR #107 / branch `feature/106-rank-admin-notifications` stacks on #105. Successful give/take/set/reset operations derive previous/current total score from returned durable adjustments plus one combat snapshot, then reuse the session-safe transition notifier.
- Notification work is best-effort after the atomic mutation/audit and cannot turn a committed command into a reported mutation failure. Tests cover promotion, demotion, within-rank suppression, disablement, failure isolation, mutation failure, disposal and adjustment-before-floor behavior. CI #372 passed build, tests including MariaDB, formatting, publish and package validation on reviewed code head `0d7256bfc311783e94a59e652b0effcad85cb265`.
- Review corrected the score order for negative raw combat totals so notifications now match rank queries exactly; no known code blocker remains. Native chat delivery requires the user's CS2/DatHost acceptance. Rank tags and menus remain open.

## Combat rank notification checkpoint — 2026-09-27

- Issue #104 / draft PR #105 / branch `feature/104-rank-transition-notifications` stacks on #103. Durable combat writes snapshot affected adjusted scores, serialize local callbacks, reuse the shared transition evaluator and schedule session-safe promotion/demotion chat messages.
- Tests cover promotion, demotion, multiple affected players, replay/within-rank suppression, disabled notifications, cancellation and disposal. CI #367 passed build, tests including MariaDB, formatting, publish and package validation on reviewed code head `77933afb5f497c916f8c4cab6980c2e0a4b2cbbc`. Review fixed notifier ownership on activation failure and suppression of queued messages after unload; no known code blocker remains.
- Native chat delivery and the underlying combat event mapping require the user's CS2/DatHost acceptance. Administrative-change notifications, rank tags and menus remain open.

## Rank administration commands checkpoint — 2026-09-26

- Issue #102 / draft PR #103 / branch `feature/102-rank-admin-commands` stacks on #101. Four explicit give/take/set/reset commands use per-operation permissions, shared online/offline target resolution, immunity and the atomic audited service.
- Tests cover descriptors, routing, default/quoted reasons, permission-before-resolution, immune targets, registration rollback and unload cleanup. CI #363 passed build, tests including MariaDB, formatting, publish and package validation on reviewed code head `cbf3a8c364e525c119884b5d94953c5900c9563f`. Review found no remaining code blocker; verify final documentation-only head CI.
- Native command dispatch still requires the user's CS2/DatHost acceptance. Rank notifications, tags and menus remain open.

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
