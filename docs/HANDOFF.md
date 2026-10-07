# Objective and special-kill rank coverage — 2026-10-07

- #288 adds bomb pickup/drop, hostage hurt, atomic team explosion/defused-others/all-rescue awards.
- Stable team identities exclude recipient membership; replay cannot reward later joins.
- FFA suppresses team objectives; optional teamkill assist/flash penalties, weapon-family
  bonuses and bounded penetration-count scaling complete the reference policy controls.
- New weights/penalties default zero; native hooks reuse independent rank eligibility.
- Policy, identity and atomic service retry/replay regressions accompany documentation.
- #287 merged after CI #703 passed 863 tests and all checks.
- #289 initial head passed 868 tests; native initializer formatting was corrected.
- Final review also suppresses FirstBlood after mid-round reload until an observed round start.
- Final exact-head CI is required before closing #228/#275. Native acceptance remains #290.

# Optional rank scoreboard — 2026-10-07

- #286 adds default-disabled Scoreboard.SyncScore and explicit RankMode overrides.
- Selected score source and bounded threshold/point projection drive native writes.
- Captured sessions protect asynchronous refresh and world-update delivery.
- Score sync reapplies after engine changes; badge conflicts relinquish ownership.
- Unload restores only still-owned fields; queued updates stop and reconnect resets ownership.
- Steam group clan tags remain untouched. Native client rendering/halftime acceptance remains #290.
- #285 merged after CI #700 passed 858 tests and all checks.
- Remaining rank implementation scope: further objective/teamkill-assist parity.

# Eligible live rank playtime — 2026-10-07

- #284 adds disabled-by-default PlaytimeIntervalSeconds and GameplayPoints.PlaytimeInterval.
- Five-second native samples accrue only consecutive eligible active-team human time.
- No historical/offline backfill; late samples are capped, reconnect/reload reset partial time.
- Frozen interval identity/player/time/context retries through the shared rank ledger.
- VIP and committed point/threshold consumers reuse existing paths; XP/stats stay independent.
- Timer is killed on unload and activation failure. Exact-head CI required before merge.
- #283 merged after CI #698 passed 852 tests and all package checks.
- Remaining reference gaps: scoreboard presentation and additional objective parity.

# Committed rank point presentation — 2026-10-07

- #282 adds opt-in NotifyPointChanges and RoundPointSummaries, disabled by default.
- Persisted player toggles independently control individual notices and summaries.
- Effective visible score changes drive messages; zero deltas are silent.
- Session checks span preference reads and delivery; post-commit failures are isolated.
- Completion waits for queued scoring writes, runs after native same-tick hooks and
  emits once; late events retain individual notices without reopening summaries.
- Bounded ephemeral round state, registration rollback and unload cleanup are covered.
- Original LiveRankScoringService constructor remains compatible.
- Exact-head CI is required before merge; real native timing remains unverified.

# Independent live rank scoring — 2026-10-07

- #279 is merged after full CI #691 passed 834 tests and package checks.
- #280 adds explicit opt-in Source=EventLedger while preserving derived defaults.
- Rank-native hooks independently gate warmup/min-human/bot/FFA policy, combine
  kill/death/assist/special/weapon/distance bonuses, dynamic ratios, positive VIP
  rewards and timed session/round-scoped streaks into atomic event batches.
- Replay checks precede recalculation; failed writes leave streak/first-blood state
  untouched. Committed awards drive session-pinned preference-aware notices.
- Native composition, lifecycle cleanup and independent round/match/objective
  hooks are integrated. Rank points never mix with progression XP.
- Automated policy/service/preference regressions accompany docs/rank-event-scoring.md.
- #281 merged after CI #696 passed 846 tests and all package checks.
- #282 implements committed point notices and ordered round summaries; playtime,
  scoreboard and further objective parity remain #228. Native acceptance remains #290.

# Durable rank event foundation — 2026-10-07

- #278 adds bounded atomic replay-safe rank batches and migration 018.
- RankScoreWeights selects derived statistics (legacy default) or the event ledger.
- Raw scores, placements and toplists consistently use the chosen source, baseline
  and existing audited adjustments; statistics reset leaves rank events intact.
- Shared runtime exposes the repository; no live configuration switch is introduced
  before policy/native composition under #275 is implemented.
- Contract and MariaDB regression tests cover concurrency, conflicting replay,
  rollback/retry, source isolation, adjustments, floors, profiles and placements.
- See docs/rank-event-scoring.md. Exact-head CI is the executable quality gate.

# Permanent achievement prerequisites — 2026-10-07

- #276 adds optional exact-ID/tier prerequisites to permanent achievement catalogs.
- Bounded immutable snapshots reject unknown tiers, null lists and dependency cycles.
- Parent-first checkpoints and player-account-locked durable reads gate atomic rewards.
- Existing catalogs stay compatible; statistic resets retain permanent unlocks.
- Definition/catalog, live module and concurrent MariaDB regression tests are added.
- Local .NET SDK is unavailable; exact-head CI remains the executable quality gate.
- Rank policy #228/#275 and native CS2 acceptance remain outstanding.

# HTTPS management sidecar — 2026-10-06

- #222 adds a separate ASP.NET Core host over the existing local ManagementPipeClient.
- Non-HTTPS, query-string, unsupported-route/method, oversized-body and malformed-header requests fail before pipe access.
- Only Authorization, X-AnoCore-Token and X-Correlation-ID cross the pipe; forwarded headers/cookies are ignored and remote identity is the direct peer only.
- Kestrel request bounds, per-peer fixed-window limiting and independent pipe connect/exchange deadlines are configured.
- The sidecar publishes independently in CI and is checked not to ship CounterStrikeSharp.API.dll.
- Unit acceptance covers TLS enforcement, forwarding boundaries, body limits, unavailable pipe, response header allowlisting and secret non-reflection.

# Configurable starting rank points — 2026-10-06

- #272 adds a bounded optional `StartingPoints` baseline (default zero) to the shared rank score contract.
- Non-zero baselines include registered profiles without score events; zero preserves prior participant semantics.
- Baseline, combat/gameplay score and audited adjustments combine before zero flooring in placement/toplists/raw-score consumers.
- Legacy `RankScoreWeights` construction remains source-compatible; tests cover bounds, profile-only players, penalties, reset, ties, pagination and zero-default upgrade behavior.
- Remaining #228 policy gaps are dynamic/VIP multipliers, streak/distance/time bonuses, summaries and native scoreboard presentation.

# Live seasons and historical standings — 2026-10-06

- #270 composes opt-in seasons.json and global reward reconciliation into the plugin.
- Own/current/history XP, paginated catalog/toplist and permission-gated explicit close
  commands reuse durable accepted versions and independent season accounts.
- Nine module tests cover configuration, startup, commands, session identity, closure
  authorization/window, global isolation, overlap/unload and registration rollback.
- #266 and #268 are merged with green full CI (795 and 802 tests respectively).
- Native CS2/DatHost acceptance remains manual; rank policy gaps remain open.

# Season reward routing backend — 2026-10-06

- #268 copies committed earned lifetime grants to accepted independent season accounts.
- Original event/amount/boost metadata is preserved; administration is excluded.
- Global bounded reconciliation includes offline players and late grants until closure.
- Seven MariaDB tests cover routing/retry/concurrency/closure/failure/rankings/bounds.
- Live season configuration, timer, own/history commands and leaderboards remain next.

# Challenge completion notices — 2026-10-06

- #266 composes committed-only session-pinned notices into the live challenge module.
- Independent persistent toggle: progression.challenge-notifications (default true).
- Six new tests cover replay, actual award XP, preference, reconnect/dispose, failures,
  cancellation and module/registration ownership. Native acceptance remains manual.
- Season routing/presentation/leaderboards and rank policy gaps remain open.

# Recurring weekend gameplay boosts — 2026-10-06

- #264 adds optional WeekendMultiplier (default 1/off; 2 enables double gameplay XP).
- Windows are Saturday 00:00 through Monday 00:00 UTC; original event time governs
  delayed processing. Stable occurrence IDs are stored in the common grant ledger.
- The strongest eligible scheduled/recurring boost wins; ordinal ID breaks ties.
- Four tests cover boundaries/offsets, overlaps/ties, bounds/snapshots and delayed
  idempotent MariaDB payout. Rank points and challenge/achievement rewards stay separate.
- Gameplay XP #262 is merged with green CI #669 (785 tests and full package checks).
- Season routing/history/leaderboards, progression notices and rank policies remain open.

# Live gameplay XP — 2026-10-06

- #262 adds configurable kill/assist/objective XP from existing accepted raw ledgers.
- Default persisted EarnFromUtc starts eligibility at configuration creation, avoiding
  accidental full-history payout. Explicitly earlier starts opt into historical data.
- Batches are bounded to 100 events/player and atomic through the common account lock.
- Stable event/role IDs preserve retry/concurrency safety; boosts use original event time.
- !anoxp is independent from permanent achievement enablement and competitive ranks.
- Startup/checkpoint/unload ownership and session safety are composed in the plugin.
- Eight MariaDB and six unit tests cover eligibility, batching/late events, retries,
  concurrency, rollback, overflow, boosts, config persistence, failure isolation and unload.
- Native tests remain manual. Season routing/history/leaderboards, recurring weekend
  generation, progression notices and remaining rank policies are still open.

# Live daily/weekly/season challenge integration — 2026-10-06

- #260 loads immutable bounded `challenges.json` schedules. Defaults are three weekly
  headshot/round-win/bomb-plant tasks; UTC daily and Monday-weekly rollover is automatic.
- Dated predefined daily/weekly/season tasks share the same durable repository.
- `!anochallenges [page]` shows session-checked active progress and expiry.
- Shared `achievements.json` XP definitions apply; rank points are unchanged.
- Non-overlapping checkpoints respect dependency order, isolate failures and cancel
  on unload. Pending/active ownership and native repeat timers are integrated.
- Eight unit tests cover snapshots, UTC windows, command/session safety, dependency
  ordering, failures, overlap/unload and registration collision. CI is required.
- Challenge notices, gameplay XP, season reward routing/history/leaderboards and
  remaining rank policies are still open. Native CS2 tests remain manual.

# Durable challenge rewards — 2026-10-06

- #258 adds window-scoped reads from the raw gameplay ledger and migration 017.
- Stable ID + UTC start identify each occurrence; stored completions retain their
  original definition version and atomic lifetime-XP grant across retries.
- Shared account locks serialize completion/prerequisite checks and payouts.
- Twelve MariaDB tests cover boundaries, future events, player/stat isolation, restart,
  concurrency, prerequisites, expiry, rollback, overflow, boost eligibility, resets,
  orphan grants and changed-window rejection.
- Local .NET remains unavailable; full CI is the executable gate.
- Live catalogs/scheduling, player UI, challenge notifications and season reward
  routing remain the next integration package, alongside gameplay XP/rank gaps.

# Achievement notifications — 2026-10-06

- #256 adds committed-only, session-pinned chat notices with actual awarded XP.
- Persistent `progression.notifications` is exposed through the shared toggle catalog.
- Preference/transport failures cannot stop awards; chat delivery is best effort.
- Tests cover preference, reconnect, unload, tier failure isolation and module wiring.
- Gameplay XP, challenge persistence, season/history UI and rank gaps remain open.

# Optional Leetify context checkpoint — 2026-10-06

- #242 / branch `feature/242-leetify-live-context` adds a separate
  `!anoleetify <player>` command; internal `!anorating` remains offline-only and
  its formula is unchanged.
- The provider uses the current public `GET /v3/profile` SteamID64 path, reads its
  API key only from `ANOCORE_LEETIFY_API_KEY`, pins the official HTTPS host and
  does not follow redirects.
- Requests are bounded to 3 seconds, 128 KiB, two concurrent lookups and 30 outbound
  requests per minute; saturated concurrency fails fast. Provider
  failures, non-users/private profiles and rate limits fail closed without
  degrading internal rating.
- Only Aim, Positioning and Utility are shown. API numeric text is validated but
  not renamed/rescaled/recalculated; output includes `Data Provided by Leetify`
  and a `View on Leetify` profile link.
- No Leetify response is persisted or cached. Current API/guideline/privacy sources
  were rechecked on 2026-10-06 and must be rechecked before release.
- Automated tests cover request/auth shape, response bounds, status/error handling,
  timeout/cancellation, concurrency, session safety, attribution and composition
  rollback. Local test execution is unavailable in the current tool container
  because it cannot resolve GitHub; PR CI is the executable gate.
- Native CS2 chat/link presentation and one disposable live-key check remain
  real-server acceptance items under #23/#230.

# Challenge catalog/evaluation checkpoint — 2026-10-06

- #252 adds immutable versioned daily, weekly and season challenge definitions.
- Daily windows are exactly 24 hours, weekly windows exactly seven days and season
  windows are arbitrary non-empty UTC ranges; all use [start, end) semantics.
- Predicates reuse existing `GameplayStatKind` values. The evaluator accepts
  window-scoped totals supplied by the caller and creates no duplicate stat counters.
- Stable prerequisites are validated as a bounded acyclic graph. A ready prerequisite
  does not unlock dependents until its completion is actually committed.
- Evaluation distinguishes future, locked, active, ready-to-complete, completed and
  expired states, with deterministic catalog ordering and bounded inputs.
- Durable challenge completion/progress baselines, XP payout, events and native UI
  remain the next #229 packages.

# Live permanent achievements — 2026-10-06

- #254 loads validated `config/achievements.json` and composes permanent
  achievements through the existing database/statistics/command services.
- Default lifetime catalogs grant retroactive headshot, round-win and bomb-plant
  tiers; no competitive rank changes. Startup/repeating checkpoints reuse stats,
  skip unchanged session totals, isolate player failures and retry failed checks.
- `!anolevel` and paginated `!anoachievements` use session-checked command output.
- Pending/active composition ownership, timer cleanup, cancellation and packaged
  progression DLLs are integrated into the native plugin lifecycle.
- Gameplay XP and challenge/season presentation remain open. See progression.md.

# Atomic achievement rewards — 2026-10-06

- #248 commits permanent unlocks and XP ledger rewards in the same transaction,
  reusing `IDatabase` and the common progression account lock/grant implementation.
- Migration 016 follows the now integrated season-XP migration 015.
- Eight MariaDB tests cover replay/restart, concurrent evaluators, stats reset,
  complete-batch rollback, overflow, boost opt-in, orphan grant collisions and
  independent player identities. Local SDK remains unavailable; CI is required.
- No separate DB connection or production credential is added. Catalog loading,
  native event wiring and player UI remain the next integration boundary.

# Season XP persistence checkpoint — 2026-10-06

- #243 / branch `feature/243-season-xp-persistence-v2` adds isolated durable
  season accounts and an idempotent season grant ledger in migration 015.
- Grants resolve against the accepted effective season catalog, enforce half-open UTC
  windows, reject new writes after closure and keep committed retries idempotent.
- Gameplay/reward XP reuses scheduled boost definitions; administrative season
  adjustments are unboosted and cannot reduce season XP below zero.
- Season levels are derived from the shared XP curve. Lifetime XP and competitive
  rank points remain untouched.
- Shared definition locks allow concurrent grants while preventing closure races.
- MariaDB coverage includes retry/restart, boundaries, closure, negative admin
  adjustment floor, duplicate concurrency, conflicting retry, rollback, historical
  reads, derived levels and lifetime isolation.
- Challenges, durable achievement unlocks, leaderboard/result snapshots and native
  presentation remain follow-ups under #229.

# Permanent achievement evaluation — 2026-10-06

- #244 adds immutable versioned achievement tiers backed by existing lifetime
  gameplay aggregates. It returns unawarded tier candidates and preserves prior
  permanent unlocks across statistic resets.
- No unlock or XP mutation is performed by the evaluator. Transactional unlock
  persistence/XP grants, prerequisites, combat metrics and live composition remain
  under #229. Candidate evaluation alone is not a concurrency/idempotency guarantee.
- Eight tests cover boundaries, tier jumps, awarded-state suppression, statistic
  reset, immutable snapshots and malformed inputs. Local .NET SDK is unavailable;
  executable verification remains pending.

# Season catalog lifecycle checkpoint — 2026-10-06

- #240 / branch `feature/240-season-catalog-lifecycle` adds bounded versioned
  season definitions, deterministic current/previous/next lookup and non-overlap
  validation using UTC half-open windows.
- Migration 014 persists every accepted definition version plus acceptance/closure
  timestamps. Highest accepted version per stable season ID is effective; older
  versions remain readable.
- Definition acceptance is immutable and idempotent. New definitions/revisions must
  be accepted before their relevant season start; retries of an already accepted
  snapshot remain idempotent even after start.
- A singleton persistence lock serializes catalog mutation so concurrent overlapping
  season accepts cannot both commit.
- Season closure is end-boundary checked, idempotent and restart-safe; the first
  closure timestamp remains authoritative and definitions are retained.
- Automated coverage includes UTC boundaries/gaps, caller isolation, invalid and
  overlapping catalogs, restart restore, version conflicts/supersession, concurrent
  overlap, close idempotency and late-first-acceptance regression.
- Season player XP, challenge/achievement evaluation, historical leaderboard/result
  snapshots, commands and native CounterStrikeSharp presentation remain follow-ups
  under #229.

# Lifetime progression persistence checkpoint — 2026-10-06

- #238 / branch `feature/238-lifetime-xp-persistence` builds on merged #236 with
  durable lifetime XP and an append-only per-player grant ledger.
- Stable grant IDs are idempotent across retry/reconnect/restart. A retry returns
  the stored award before current boost definitions are reevaluated; reusing the
  ID with a different original payload fails explicitly.
- Migration 013 stores account lifetime XP/revision plus bounded source, base/final
  XP, reason, UTC occurrence time and selected boost metadata for every grant.
- Same-player writes serialize on the account row. Ledger insert and cumulative XP
  update share one transaction; checked arithmetic and transaction failures roll
  back without a partial total.
- Lifetime level is derived from the accepted level-definition snapshot and is not
  stored as a second score. Competitive rank points remain completely untouched.
- Automated coverage includes restart retry, changed-definition retry, conflicting
  IDs, concurrent same/different grants, forced database rollback, overflow and
  migration idempotency.
- Native gameplay-event composition, season XP, achievements/challenges, commands
  and administrative adjustment surfaces remain follow-up packages under #229.

# Progression definitions checkpoint — 2026-10-06

- #236 / branch `feature/236-progression-levels-boosts` adds the first
  engine-independent progression package: immutable level/boost definition snapshots,
  bounded consecutive XP thresholds and deterministic XP-to-level lookup.
- Scheduled boost windows are UTC-only and half-open (`[start, end)`). Overlap uses
  the highest eligible multiplier with deterministic ID tie-breaking.
- Gameplay XP is boost-eligible by default. Challenge and achievement reward XP must
  be explicitly opted into a boost definition; administrative adjustments are never
  multiplied by this schedule.
- Integer XP awards use decimal multiplication followed by truncation toward zero and
  checked conversion, so fractional results and overflow have explicit behavior.
- This package intentionally does not add persistence, seasons, challenges,
  achievements, commands or CounterStrikeSharp composition. Those remain follow-up
  packages under #229 after this definition contract is merged.
- Contract tests cover level boundaries, caller-mutation isolation, UTC window
  boundaries/overlap, payout isolation, invalid definitions and overflow.

# Progression and rating scope — 2026-10-06

- #229 adds the accepted independent progression workstream: lifetime XP/levels,
  season XP/levels, permanent achievements, daily/weekly/season challenges,
  predefined season catalogs and scheduled XP modifiers such as double-XP weekends.
- Progression must never read or write the competitive rank-point score and must
  reuse existing gameplay/stat signals rather than duplicate event counting.
- #234 / PR #235 implements the internal versioned `!anorating` list/detail views,
  multidimensional estimates, provisional/unscored state and observed-sample confidence.
  #230 remains open for optional Leetify context. Automatic balancing and FACEIT
  remain outside scope.
- Leetify is the only accepted optional external player-context provider. Its data
  stays separate from AnoRating, is not persisted in AnoCore, is displayed without
  renaming/rescaling/recalculation, and requires current attribution/compliance
  checks before release enablement.
- These are post-foundation product workstreams and must follow the normal
  Issue -> branch -> tests -> implementation -> docs -> PR -> CI -> review flow.

# Reference parity follow-up — 2026-10-06

- Reference checked: K4ryuu/K4-Zenith `dev` commit
  `94cd8fb34ffa2121d07ec7846dbcb9c1ac37935e`.
- #224 is merged: local management clients now bound response exchanges separately
  from connection establishment. CI #610 passed before merge.
- #226 / PR #227 adds periodic playtime notices, validated server configuration and
  the persisted `playtime.notifications` toggle through existing settings UI.
  Notices read totals after checkpointing and use session-pinned shared messaging.
  PR #227 merged after complete CI #613 passed.
- #232 / PR #233 merged after complete CI #618 passed. Ranks now support bounded
  optional event bonuses/penalties across all existing score views and notifications,
  reusing effective gameplay events without duplicate counters. Empty defaults preserve scores.
- #228 retains dynamic multipliers, VIP, streak/distance/time policy, summaries and
  native scoreboard rank presentation. These are implementation gaps, not only native tests.
- #225 was withdrawn at the user's direction. CS2 native Steam group clan-tag
  selection/display is retained; no new clan-tag override implementation was
  published. CounterStrikeSharp latest release is 1.0.376 and already pinned.
- Current scope: continue implementation, integration, automated tests and docs
  without waiting for user-run CS2/DatHost tests. Keep native verification evidence
  separate and do not claim production verification from automated checks.

# Consolidated main checkpoint — 2026-10-04

- `main` has been consolidated through #209 and then hardened with #211 (management status rate-limit/audit/exception guards) and #212 (Tournament-AnoVeto coordination).
- The current code-complete stack includes persistent player/settings/moderation/statistics/ranks/playtime, combined statistics and settings/rank/tag menus, audited statistics reset, extended administration, CustomHud/AnoVeto, tournament state/persistence/reconnect enforcement/control/demo-backup/spectator policy/map selection, the packaged module SDK and the authenticated transport-neutral management API core.
- Tournament map selection now reuses the existing AnoVeto map catalog and vote service. Auto-finalized vote results are consumable, stale match/revision results are rejected, and the resolved series is persisted before it is published to the live tournament runtime. Manual `anotournamentmaps` remains the fallback.
- Management status reads now receive the same security treatment as privileged management capabilities: authenticated scope checks, bounded per-token rate limiting, metadata-only request/result audit and redacted provider failures.
- `docs/functional-acceptance.md` is the current functional inventory. Historical open issues and old draft PRs are not reliable indicators that a feature is absent because many packages were first merged into the non-production integration branch and later consolidated into `main`.
- Remaining work must be split into two categories:
  1. actual missing code/packages (after #216, primarily any remaining external transport/integration surface and gaps found by acceptance); and
  2. native-only acceptance gates in #23 / the disposable CS2/DatHost flow (CounterStrikeSharp event semantics, real menu/HUD/message interaction, map transitions, voice/chat behavior, tournament engine actions, deployment/upgrade).
- Do not claim production readiness or cut a release solely from automated CI. Native acceptance still has to be recorded against the exact deployment artifact before #23 and #11 can close.

## Recent verification

- PR #211 exact-head CI #592: build, full tests including MariaDB, formatting, SDK pack inspection, Panorama validation, plugin publish/package and artifact upload all passed before merge to `main`.
- PR #212 exact-head CI #593 passed the same full workflow against the security-corrected `main` before merge.
- The previous integration Tournament-AnoVeto PR #210 exact-head CI #589 was also green before its mainline follow-up.

---

# Management HTTP adapter checkpoint — 2026-10-04

- Issue #218 / branch `feature/218-management-http-adapter` adds the bounded HTTP v1 wire adapter over the existing management gateway without starting a listener or adding ASP.NET/Kestrel to the CS2 plugin.
- Status and explicit capability routes share the existing credential verification, scopes, rate limits and audit behavior. The adapter adds bounded token/correlation/auth headers, pre-parse body/path/header limits, deterministic JSON and conservative HTTP status mapping.
- Operation bodies accept only a flat bounded string dictionary. Credentials, exception text and unvalidated request data are not echoed.
- Tests cover successful status/operation calls, wrong credentials, missing correlation, scope denial, malformed/oversized input, version/route handling and rate limiting.
- A TLS/network host and deployment-specific secret provisioning remain separate #22 composition work; the transport contract itself no longer needs to be reinvented by each host.

# Shared messaging checkpoint — 2026-10-04

- Issue #216 / branch `feature/216-shared-messaging` adds the public `IMessageService` contract, player/team/all targets and chat/center/center-HTML channels.
- The runtime owns timed center-message priority leases. Lower priority output is suppressed, replacement is versioned, and an older expiry cannot clear a newer message.
- The CounterStrikeSharp adapter resolves recipients from the shared player registry on the server thread and supports session-pinned player targets so reconnects reject stale queued output.
- Localization and placeholders remain pre-dispatch concerns; modules receive one engine-independent native output path without taking a CounterStrikeSharp dependency.
- Automated tests cover validation, transport absence, suppression, replacement, expiry and failed-delivery slot release. Native rendering/duration/reload behavior remains #23 acceptance.

# Extended administration integration checkpoint — 2026-10-04

- The #157 candidate now combines the #165/#166/#167/#168 stack with the existing rank notification preference, stats/chat/settings/moderation and CustomHud stacks.
- Review correction #176 invalidates disconnected registry sessions before waiting for admin cleanup; regression tests cover a blocked action with reconnect and failed event observers.
- Composition conflicts preserve both command families, combat death recording plus state cleanup, pre-death position capture and map-end cleanup.
- The individual admin PRs #172/#173/#174 remain native acceptance candidates. This integration does not close their live gates or promote the full system to main.
- Use docs/full-system-test.md for the combined build; full statistics/playtime policies, tournament and platform scope remain outstanding.

# Rank notification preference checkpoint — 2026-10-04

- Issue #170 / branch `feature/170-rank-notification-toggle` gives the #157 integration candidate its first real player-toggle option: `rank.notifications`, default enabled.
- The persisted preference filters combat-driven and administrative promotion/demotion presentation only; durable score/admin writes, rank evaluation and warmed rank/chat refreshes continue unchanged. Server-level rank notification switches remain authoritative.
- Rank composition owns the toggle registration and rolls it back on failure/unload. Preference reads are asynchronous outside the native chat hot path, and read failures are isolated from committed gameplay mutations.
- PR #171 targets `integration/157-full-system-test`. Automated CI must pass on the exact head; actual settings UI and native notification delivery remain part of the disposable CS2/DatHost acceptance gate in #157.

# Full-system disposable test checkpoint — 2026-10-02

- Issue #157 / branch `integration/157-full-system-test` assembles the previously independent code-complete native stacks without promoting their unverified behavior to `main`.
- The candidate currently combines playtime/combat/toplists/ranks, rank administration and notifications, formatted chat/colors/tags, settings commands/menu, connect-ban, kick, warnings, CustomHud AnoVeto with live config reload, and the packaged module SDK.
- Merge conflicts were resolved by preserving newer core/config/settings contracts while adding the feature stacks; AnoVeto keeps both Panorama CustomHud presentation and the newer atomic `anoveto`/`maps` reload registrations.
- Use `docs/full-system-test.md` and only the exact green CI artifact from the integration PR. Native CS2/DatHost observations remain release gates.

# Module SDK packaging checkpoint — 2026-10-02

- Issue #155 / branch `feature/155-abstractions-sdk-package` packages `AnoCore.Abstractions` as the prerelease module SDK and keeps the module build surface isolated from Runtime, Plugin, CounterStrikeSharp and MySqlConnector.
- The package includes generated XML docs, license, notice and a compile-correct minimal module README. CI packs and inspects the nupkg before adding it to the existing `AnoCore-development` artifact.
- Executable assembly-reference coverage plus nupkg metadata/content checks protect the dependency boundary. No native CS2/DatHost acceptance is required for this package.
- Wider external module discovery, Web/API/server-management work and native feature acceptance remain under #22 / #11.

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
