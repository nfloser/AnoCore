# Progression definition core

Issue #236 establishes the deterministic definition layer for AnoCore progression.
It is deliberately independent from competitive rank points, storage and the
CounterStrikeSharp runtime.

## Level curve

A definition snapshot contains between 1 and 500 level thresholds.

- Levels are consecutive and start at level 1.
- Level 1 starts at 0 XP.
- Minimum XP values are nonnegative and strictly increasing.
- XP-to-level lookup uses the highest threshold whose minimum XP is less than or
  equal to the player's XP.
- Negative XP is rejected.
- The snapshot copies caller collections so later configuration mutations cannot
  change an already accepted definition set.

These rules make a level curve stable enough to reference from later persisted
lifetime and season progression without silently reinterpreting a mutable object.

## Scheduled XP boosts

A boost has a stable ID, UTC start/end timestamps, a decimal multiplier and an
explicit set of eligible XP sources.

Windows use half-open semantics:

```text
start <= award time < end
```

Definitions must use zero-offset UTC timestamps. Runtime award timestamps are
normalized to UTC before evaluation. If multiple eligible boosts overlap, AnoCore
uses only the highest multiplier. Equal multipliers are resolved by stable ordinal
ID ordering so the selected display identity is deterministic.

Multipliers are bounded from 1x through 10x. Boost IDs are unique
case-insensitively and definition counts are bounded.

## Award-source isolation

Gameplay is the only boost-eligible source by default. A specific boost may opt
challenge rewards and/or achievement rewards in explicitly.

Administrative XP adjustments are never multiplied by the scheduled boost layer.

This prevents a double-XP weekend from accidentally doubling fixed challenge or
achievement payouts while still allowing a future season definition to opt a
specific reward class into a special event.

## Integer XP arithmetic

XP remains an integer `long`. Award calculation multiplies with `decimal`,
truncates a positive fractional result toward zero and performs a checked
conversion back to `long`.

Negative base awards are rejected and overflow raises `OverflowException`.
Later idempotent grant/persistence code must treat that failure as a rejected
award rather than partially committing progression state.

## Current boundary

The definition layer remains engine-independent. Durable lifetime XP and the
versioned season catalog/lifecycle are added by separate persistence packages
described below; season XP, challenge/achievement evaluation, player commands and
native CS2 composition remain separate packages under #229.


## Durable lifetime XP grants

Issue #238 adds the first persistent progression state. It deliberately stores only
lifetime XP and the immutable facts of each accepted grant; season state remains a
separate follow-up.

Each player has one progression account row with cumulative lifetime XP and a
monotonic revision. Every award also writes a ledger row keyed by
`(SteamID64, grant ID)`. The grant ID must be stable for the source event so a
retry, reconnect or process restart can identify the same logical award.

A grant records:

- source and original base XP;
- final awarded XP;
- bounded reason identifier;
- normalized UTC event timestamp;
- selected boost ID and multiplier;
- lifetime XP and account revision after the award.

The ledger insert and account update occur in one MariaDB transaction while the
player account row is locked. Concurrent grants for the same player therefore
serialize, while unrelated players do not share that lock. Arithmetic is checked;
a failed insert, overflow or other transaction failure cannot leave a partially
updated lifetime total.

### Retry semantics

The service checks an existing grant before resolving current boost definitions.
If the original source, base XP, reason and normalized event timestamp match, the
persisted result is returned without recalculating the old award. This is important
when a server restarts after configuration has changed: a retry must not silently
turn an earlier 2x award into a later 3x award.

If the same grant ID is reused with a different original payload, the operation
fails with `ProgressionGrantConflictException`. A second in-flight writer is also
checked inside the locked transaction, so the pre-read is an optimization rather
than the idempotency guarantee.

Lifetime level is derived from the currently accepted immutable level definition
snapshot and is not stored as a second mutable score. Competitive rank points are
neither read nor written by this path.

### Persistence boundary

Migration 013 creates `ano_progression_accounts` and
`ano_progression_grants`. The persistence assembly is intentionally not composed
into the CounterStrikeSharp plugin yet. Gameplay-event wiring, player-facing
commands, administration, season XP, challenges and achievements remain separate
packages under #229.


## Durable season catalog and lifecycle

Issue #240 adds the deterministic season-definition and lifecycle layer without
adding season player XP yet.

A season definition has a stable ASCII ID, positive definition version, bounded
display name and a UTC-only half-open window `[start, end)`. Effective definitions
are ordered by start time and cannot overlap, so resolving current, previous and
next season at any instant is deterministic, including exact boundary timestamps
and gaps between seasons.

Accepted definitions are immutable snapshots. Re-accepting the same ID/version
with identical content is idempotent; changing an already accepted ID/version is a
conflict. A higher version may supersede the effective definition only before both
the previously accepted window and the replacement window have started. A brand-new
definition must also be accepted before its configured start. This still allows a
retry of an already accepted definition after start to return the stored snapshot
without rewriting history.

Migration 014 stores every accepted version plus acceptance and optional closure
timestamps. A singleton season-runtime row serializes catalog mutation so concurrent
overlapping accepts cannot both commit. The effective catalog uses the highest
accepted version of each stable season ID, while older versions remain readable for
history and audit.

Closing a season is explicit and idempotent. Closure cannot occur before the
configured end; the first committed closure timestamp remains authoritative across
retry and restart. Definitions are never deleted by closure, preserving the basis
for later historical leaderboard/result snapshots.

This package still does not add per-player season XP, season grants, challenge
evaluation, leaderboards or CounterStrikeSharp presentation. Those remain separate
reviewable packages under #229.

## Permanent achievement tier evaluation

Issue #244 adds `AchievementDefinition`, an immutable, versioned definition for
one permanent achievement backed by an existing `GameplayStatKind` aggregate.
For example, `HeadshotKill` with targets 10, 50 and 100 can award 100, 200 and
300 XP respectively. A one-time achievement uses just one tier.

IDs contain 1–64 ASCII letters, digits, dots, dashes or underscores. Versions are
positive. Definitions contain 1–100 consecutive tiers with strictly increasing
positive targets and nonnegative reward XP. Definitions snapshot the tier list
and expose read-only collections. Enumeration is bounded during validation.

Evaluation consumes the existing lifetime gameplay totals and the highest tier
already durably awarded. Missing kinds count as zero; duplicate kinds, unknown
kinds, null entries and negative counts are rejected. Unrelated valid totals are
ignored. Crossing multiple targets returns every unawarded tier in order. Passing
the committed awarded tier on retry returns no duplicate unlock candidates.
Previously awarded tiers remain unlocked even after a statistics reset.

This evaluator does not persist unlocks, award XP or send notifications. Its output
is a set of **candidates**, not proof that rewards were committed. The future
integration must lock player achievement state and atomically persist each unlock
with its XP grant, using a stable identity such as `(player, achievement ID, tier)`.
Store the definition version with that unlock. Definition changes must preserve
already awarded tier identity and require an explicit migration policy when
removing/reordering tiers. Concurrent evaluators alone do not guarantee idempotency.

Prerequisites, combat totals such as kills/assists, daily/weekly challenge windows,
menus and plugin composition remain separate #229 packages. This package creates
no duplicate statistic counter and does not access competitive rank points.


## Durable season XP

Issue #243 adds isolated per-player season XP on top of the accepted season
catalog. Season state is keyed by SteamID64 plus stable season ID and records the
accepted definition version used for grants. Season XP never reads or writes
lifetime XP or competitive rank points.

Migration 015 adds season accounts and an idempotent grant ledger. Grants are
accepted only for the effective accepted season whose half-open UTC window contains
the event timestamp. New grants are rejected after explicit season closure, while
retries of an already committed grant remain idempotent. Gameplay/reward grants use
the shared scheduled boost resolver; administrative adjustments are never boosted
and may reduce season XP, but never below zero.

Season definition rows are held with a shared lock during a commit. That keeps
ordinary grants concurrent while ensuring explicit season closure cannot overtake a
grant that is being committed. Per-player account rows still serialize competing
writes for one player's season total.

Season levels are derived from the existing immutable XP threshold definition rather
than persisted as mutable state. Historical season state remains addressable by
season ID/version, while current-season reads resolve against the accepted effective
catalog. Challenge evaluation, durable achievement unlock/reward persistence,
season leaderboards/result snapshots and CounterStrikeSharp presentation remain
separate follow-up packages under #229.

## Atomic permanent achievement rewards

Issue #248 adds `IAchievementRepository` and `MySqlAchievementRepository`.
Migration 016 creates `ano_progression_achievements` after the integrated
season-XP migration 015. Unlock identity is case-sensitive and uses
`(SteamID64, achievement ID, tier)`. Each row retains its definition version and
references the matching XP ledger grant. The shared progression bootstrap applies
the achievement migration idempotently.

`UnlockAsync` snapshots/validates existing statistic totals, locks the existing
progression account, reads committed permanent tiers and reevaluates candidates.
It commits every newly reached tier, its `AchievementReward` ledger record and
lifetime XP update in **one transaction**. It reuses the existing grant transaction
implementation rather than introducing another XP store. Concurrent calls serialize
on the same player-account lock. No grant or unlock notification should be emitted
until the returned transaction has committed.

Retries, including after restart or changes to reward/boost definitions, skip
already unlocked tiers. Stored rewards are not reinterpreted or awarded again.
Statistics resets preserve unlocked tiers. Orphan ledger collisions and malformed
stored tier sequences fail closed. Any insert failure, cancellation or overflow
rolls back the entire batch, including earlier tiers in that call.

Achievement rewards remain excluded from gameplay-only double-XP windows.
Explicit achievement-eligible boosts are supported and recorded in the common
ledger. `achievement:<ID>:<tier>` is reserved for unlock grants; ordinary gameplay
or administrative integrations must not manufacture IDs in that namespace.

The repository accepts the existing `IDatabase`. Live startup already selects
`ANOCORE_MYSQL`, falling back to `config/core.json` `ConnectionString`; achievement
storage requires no separate credentials or connection. This package does not yet
compose progression into the live plugin. Validated achievement catalogs, post-commit
statistic wiring, player commands and notifications remain under #229. Development
CI tests use their own disposable MariaDB database, never production credentials.


## Challenge catalog and deterministic evaluation

Issue #252 adds the engine-independent challenge catalog used by future daily,
weekly and season progression. Challenge definitions have stable ASCII IDs,
positive versions, names, an existing `GameplayStatKind` predicate, positive target,
nonnegative reward XP, a UTC half-open window and an optional prerequisite list.
Daily definitions span exactly 24 hours, weekly definitions exactly seven days and
season definitions may span any non-empty UTC range.

Catalog construction snapshots definitions, bounds catalog and prerequisite sizes,
rejects missing dependencies and dependency cycles, and orders definitions
deterministically by start time, window kind and ID. No CounterStrikeSharp type is
required.

Evaluation consumes **window-scoped** `GameplayStatTotal` values. This package does
not create another gameplay counter: the persistence/integration layer is
responsible for supplying totals or baselines scoped to the challenge window from
existing durable signals. Evaluation reports future, locked, active,
ready-to-complete, completed or expired. Reaching a target only produces a
completion candidate; prerequisites become satisfied only after their challenge IDs
are present in the committed-completion set. That prevents one evaluation pass from
pretending persistence already succeeded.

Durable challenge completion/progress state, idempotent reward payout, completion
events and player-facing presentation remain separate follow-up packages under #229.

## Live permanent achievements

Issue #254 composes permanent achievements into the plugin with the shared
`IDatabase`, migration bootstrap, statistics repository and command registry.
The development plugin package includes both progression assemblies.

On first startup, `config/achievements.json` is created with:

- `Enabled: true`, `CheckpointSeconds: 30` (allowed range 10–600);
- levels 1–5 at cumulative XP 0, 100, 300, 600 and 1000;
- no scheduled boosts initially;
- headshots at 10/50/100, round wins at 10/50/100 and bomb plants at 5/25/50;
- tier rewards of 100/200/300 lifetime XP for each achievement.

The catalog supports 1–32 achievements with unique case-insensitive IDs, printable
names, positive definition versions and the previously documented bounded tiers.
Configuration is validated and copied into immutable definition snapshots.
Changes require a plugin restart; this package does not add live config reload.
Disable the feature with `Enabled: false` and restart to retain stored progression
without registering its commands or reconciliation timer.

### Existing players and checkpoint behavior

Achievements deliberately count existing lifetime statistics **retroactively**.
A player already at 100 recorded headshots receives all three previously unawarded
headshot tiers at the next reconciliation. Existing rank points are untouched.
No separate baseline or duplicate kill/objective counter is created.

The module reconciles online human players after successful runtime activation and
periodically thereafter. Each checkpoint reads existing lifetime gameplay totals
once per player. Unchanged totals in the same session skip unlock transactions.
Reconnect/restart causes another check against durable unlock state. A failed
player does not block later players; failures are not cached and retry on the next
checkpoint. Overlapping checkpoints are skipped instead of accumulating work.
Changes normally become visible within the configured checkpoint interval.

Unlock time is the reconciliation instant, not the historical timestamp of the
first statistic. Explicit achievement-eligible boosts therefore use that instant;
default gameplay-only boosts do not multiply achievement rewards. Statistics resets
retain permanent unlocks, as specified by the persistence layer.

Unload kills the timer, cancels in-flight work and unregisters owned commands.
Invalid configuration or failed progression startup is logged and isolated from
other AnoCore modules. This path does not perform XP queries inside native gameplay
callbacks. A disconnect after a durable transaction starts may still commit a valid
lifetime unlock for that account; stale player sessions do not receive command data.

### Player commands

- `!anolevel`: lifetime XP and configured XP level, independent from rank points.
- `!anoachievements [page]`: five catalog entries per page, awarded tiers and progress.

Both commands require a connected player. Responses use the existing command bridge
and are suppressed when the caller reconnects or the module unloads during a read.
The checkpoint grants rewards; opening a command does not manufacture new counters
or arbitrary XP grants.

This package makes permanent gameplay-stat achievements usable on the server.
Generic gameplay XP, combat-only achievement metrics, richer
menus, challenge/season presentation and leaderboards remain separate #229 work.
Native CS2/DatHost behavior has not been verified by the automated build/test gate.

## Permanent achievement notifications

Issue #256 sends session-pinned chat notifications only for newly committed unlock
records. The displayed XP is the actual awarded ledger amount, including any
explicitly eligible boost. Empty retry results produce no duplicate messages.
`progression.notifications` is enabled by default and registered in the existing
player toggle catalog; its value persists through the shared settings service.

Preference reads and message delivery are best effort: failures are logged without
interrupting other tiers or achievement evaluations. Notifications are not a
durable delivery queue: a crash after committing rewards can lose the message,
but cannot grant rewards twice. Reconnect and unload suppress old-session output.
Startup retroactive unlocks follow the same preference. Native CS2 chat acceptance
remains separate from automated tests.

## Durable challenge completion and rewards

Issue #258 adds `IChallengeRepository` and `MySqlChallengeRepository`. Migration
017 stores a completion keyed by player, stable challenge ID and UTC window start.
A new week/day uses a new start and can pay again. Changing a definition version or
reward for an already completed occurrence cannot pay it again; its first committed
version and XP grant remain authoritative. A completed occurrence cannot change its
end boundary. Persisted windows require microsecond precision to match DATETIME(6).

Progress is computed from the existing raw durable gameplay event ledger, filtered
by player, statistic and `[start, end)` plus an upper bound of the evaluation instant.
Future-dated events cannot complete a challenge early. No lifetime aggregate, new
counter store or reset-sensitive statistics view is used. Statistics resets preserve
challenge progress; actual deletion/purge of raw events can remove uncompleted
progress. Only events accepted by the existing gameplay ingestion policy count.

`ReadAsync` reports observational status; `CompleteAsync` reevaluates under the shared
per-player progression account lock. Prerequisites require committed completions for
their configured ID/window occurrence; last week's completion does not satisfy next
week's prerequisite. Completion and its lifetime XP grant commit in one transaction.
Concurrent/repeated calls pay once; insert failure, XP overflow and orphan ledger
collisions fail without leaving partial rewards. Default gameplay boosts do not
boost challenge rewards; explicit `ChallengeReward` eligibility does.

Completions are permitted only while the window is active. Reaching a target without
a successful completion call before expiry does not produce a late payout. Stored
completions remain completed after expiry/restart. This is a storage API, not live
challenge configuration, a scheduler, commands, notifications or season-XP routing;
those remain integration work under #229. No competitive rank mutation occurs.

## Live recurring and predefined challenges

Issue #260 loads `config/challenges.json` and registers `!anochallenges [page]`
for connected players. The default enabled configuration has three weekly tasks:
10 headshots, 10 round wins and 5 bomb plants, each with a base reward of 100 XP.
Daily windows start at 00:00 UTC; weekly windows start Monday 00:00 UTC. They follow
UTC through daylight-saving changes. A new window is a new payable occurrence.

Configuration fields:

- `Enabled` (default true) controls challenge module composition.
- `CheckpointSeconds` (default 30, valid 10-600) controls completion polling.
- `Recurring` contains at most 32 daily/weekly templates with `Id`, `Version`,
  `Name`, `WindowKind`, `Statistic`, `Target`, `RewardXp`, `PrerequisiteIds`.
- `Predefined` contains at most 96 dated `ChallengeDefinition` entries, including
  `StartsAtUtc` and `EndsAtUtc`. Season challenges use `WindowKind: 2` and arbitrary
  non-empty UTC windows. They do not automatically create/close a season account.

Enums use the existing numeric JSON representation (`Daily=0`, `Weekly=1`,
`Season=2`, `HeadshotKill=15`, `RoundWon=8`, `BombPlanted=2`). Example weekly template:

```json
{
  "Id": "weekly.headshots", "Version": 1, "Name": "Weekly headshots",
  "WindowKind": 1, "Statistic": 15, "Target": 10, "RewardXp": 100,
  "PrerequisiteIds": []
}
```

Defaults are written by the existing configuration store. Empty template and
predefined lists intentionally expose no active challenges. IDs are case-sensitive
and unique across both lists; prerequisite references and cycles are validated.
Invalid challenge configuration disables this optional module while other features
continue. Changes require plugin restart/reload. Immutable configuration snapshots
prevent later list mutations from changing an active module.

Checkpoints start after successful plugin activation, then repeat without overlap.
They attempt active challenges in prerequisite order using the existing shared
database and raw accepted gameplay events. Failures are logged per challenge and
retried on later checkpoints. Current-window events predating plugin startup count;
expired windows cannot receive a new completion. A server stopped across an expiry
therefore does not receive retrospective payouts. Pending checkpoints are cancelled
on unload and stale player sessions cannot receive command output.

The command lists up to five active challenges per page with completion/lock state,
progress, configured base reward and UTC expiry. Actual awarded XP can differ under
an explicitly eligible reward boost. XP curves and scheduled boosts reuse
`achievements.json`'s `Levels`/`Boosts`, even if its `Enabled` flag disables permanent
achievement evaluation. Invalid shared XP definitions disable challenge composition
as well. Rank points remain independent.

Challenge notifications are composed by #266; #268/#270 add season routing, historical presentation and leaderboards. Native acceptance: verify default creation, `!anochallenges`,
accepted gameplay progress, one durable reward, restart replay, Monday/daily rollover,
predefined season tasks, disabled/invalid configuration isolation and unload/reconnect
on a disposable CS2/DatHost server. Automated tests do not establish native behavior.

## Gameplay XP from accepted durable events

Issue #262 composes gameplay XP through `config/gameplay-xp.json` and the shared
combat/gameplay event ledgers. `!anoxp` shows independent lifetime XP and level,
including achievement/challenge rewards, even when permanent achievement evaluation
is disabled. Shared XP curves and scheduled boosts still come from achievements.json.

Defaults: enabled, 30-second checkpoints, at most 100 events per player per batch,
10 XP per kill, 5 per assist, plus these additive event bonuses: headshot 5, bomb
plant 20, bomb defuse 30, hostage rescue 30, MVP 10 and round win 10. Other gameplay
event weights are zero. A headshot may earn both ordinary kill XP and its bonus;
these are distinct accepted events. Deaths, suicides and teamkills do not earn kill
XP and no XP penalties are applied. Rewards do not change competitive rank points.

`EarnFromUtc` is created once when the configuration is first saved, at the current
UTC instant with microsecond precision. Keep that value across restarts. It prevents
unintentional lifetime-history payouts. Setting an earlier start deliberately opts
into historical accepted events; setting it later excludes earlier unprocessed
events. `KillXp`/`AssistXp` and each `GameplayXp` map value allow 0-1000. The map uses
existing GameplayStatKind names, like `HeadshotKill` and `BombPlanted`. Batch size
allows 1-100 and checkpoint seconds 10-600. Null/unknown/negative values, non-UTC or
sub-microsecond starts and out-of-range limits disable this optional module.

Each batch takes the shared per-player progression account lock, selects ungranted
accepted events between EarnFromUtc and the checkpoint instant, orders by event time
and stable grant ID, and atomically commits all grants in that batch. A future-dated
event cannot earn early. Grant identities include source role and original event
GUID, so kill, assist and objective rewards cannot collide. Event-ID retries,
concurrent checks and restarts do not repay committed events. Changing weights does
not rescore committed grants; previously unweighted events can become eligible
under new positive weights. Entire-batch rollback preserves retryability on insert
failure, overflow or cancellation.

Boosts resolve at the original persisted event timestamp, so delayed processing
preserves the event's scheduled boost eligibility. Already committed amounts and
boost metadata remain immutable. This supports explicitly dated double-XP weekend
windows; #264 adds optional recurring weekend boosts as described below. Raw ledgers
are used, so display/statistics-reset cutoffs do not erase earned or pending XP.
Deleting raw events can remove unprocessed rewards. Only events already accepted by
the existing combat/statistics intake policy are considered; this adds no counters
or native event ingestion calls.

Startup and non-overlapping repeating checkpoints reconcile current online players.
A failing player does not stop others. An offline player's pending events wait for
a later connected checkpoint. Large backlogs drain across multiple bounded batches;
the bound limits writes, not the cost of scanning retained eligible event history.
There is no timestamp cursor that could silently skip late-arriving old events.
Unload cancels pending transactions and stale sessions cannot receive command output.
Restart/reload is required for configuration changes. Disabled configuration retains
already earned XP. There is no per-gameplay-grant notice. #268/#270 route committed rewards into accepted seasons.

Native acceptance: create the defaults, inspect saved EarnFromUtc, earn a kill/assist
and objective bonus, verify !anoxp, restart/replay without duplicate XP, check a
configured event-time boost, teamkill/suicide behavior, backlog/reconnect and disabled
or invalid configuration isolation on a disposable CS2/DatHost server. Automated
transaction/contract tests do not establish native event behavior.

## Recurring double-XP weekends

Issue #264 adds `WeekendMultiplier` to gameplay-xp.json. The default `1` disables
recurring weekend boosts; set `2` for double gameplay XP, then restart/reload the
plugin. Values from 1 through 10 are supported. Existing files that omit the new
field keep the disabled default.

A weekend is `[Saturday 00:00 UTC, Monday 00:00 UTC)`. Calendar boundaries use UTC
even during daylight-saving changes. Boost selection uses the original event time,
including delayed/backlogged events. Each occurrence has a stable recorded ID such
as `gameplay.weekend.20261010`. Changing the multiplier can affect unprocessed
events but never changes or repays an existing committed grant.

The recurring boost competes with eligible explicit scheduled boosts from
achievements.json: the largest multiplier wins, with ordinal boost-ID ordering
on equal multipliers. Multipliers do not stack. This applies only to gameplay XP,
including configured kill/assist/objective bonuses; challenge/achievement rewards
and competitive rank points are unaffected by the recurring setting.

Acceptance: set WeekendMultiplier to 2, check Friday/Saturday/Monday UTC boundaries,
verify original-event-time processing after reconnect/restart, overlap with a
stronger explicit event, and confirm challenge rewards/rank points remain separate.
Four added tests cover UTC/offset boundaries, overlap/ties, configuration bounds and
snapshots, and delayed idempotent MariaDB payout. Native server checks remain manual.

## Challenge completion notices

Issue #266 adds private chat notices after newly committed challenge rewards. Messages
use the stored awarded XP (including eligible scheduled boosts), never an estimated
base reward. Replayed completions and unsuccessful claims produce no notice.

The persistent `progression.challenge-notifications` toggle defaults to true and is
available through the shared settings menu/commands. It remains available when
achievements are disabled. Notices are pinned to the connected player session;
reconnect, unload, preference-read and delivery failures cannot replay or undo XP.
Delivery is best effort: a message lost after commit is not replayed after restart.

Acceptance: finish a challenge, verify one own notice with the granted XP, run the
checkpoint/reconnect again, disable the toggle, verify no extra notices, and test
unload/reconnect during delivery on a disposable CS2 server. Automated tests cover
commit/replay, settings, transport failures, stale sessions and module ownership.

## Routing earned rewards into season XP

Issue #268 adds `ISeasonRewardRepository` and a MariaDB reconciler over the existing
committed lifetime grant ledger. Gameplay, challenge and achievement rewards in an
accepted season's half-open UTC window are copied into that season's independent
ledger. Administrative lifetime adjustments are excluded. Offline players are
included; event time, source, base/awarded XP and boost metadata are preserved.
Boosts are not recalculated and lifetime totals are not changed by reconciliation.

Each call reads at most 100 pending grants, ordered by original timestamp, SteamID
and grant ID. Each season grant is independently transactional. A failure can leave
a committed prefix; retry/restart discovers remaining grants without paying again.
There is no moving time cursor, so late commits with older event timestamps remain
discoverable. Queries scan the eligible season window; bound write batches do not
guarantee constant query cost as a ledger grows.

An ended but unclosed season can still drain delayed rewards. Explicit durable
closure freezes it and rejects further grants, including late arrivals. A closure
racing routing can stop the batch through the existing season transaction guard.
Operators must allow reward checkpoints/backlogs to drain before freezing winners.

Season rankings read only independent season accounts, ordered by XP descending and
SteamID ascending for ties. Pagination is bounded and placement remains global.
Closed seasons remain queryable. #270 composes the live timer and commands described below.

Seven MariaDB tests cover eligibility/metadata, late offline rewards, bounded retry,
concurrency, closure, partial failure recovery, rankings and cancellation/bounds.

## Live configured seasons and history

Issue #270 loads `config/seasons.json`. Default `Enabled: false` leaves season
configuration opt-in. Set Enabled to true and define future non-overlapping UTC
seasons before their start. CheckpointSeconds defaults to 30 (10–600); BatchSize
defaults to 100 (1–100 per unclosed started season per checkpoint). Example:

```json
{
  "Enabled": true,
  "CheckpointSeconds": 30,
  "BatchSize": 100,
  "Seasons": [
    {
      "Id": "winter-2027",
      "Version": 1,
      "Name": "Winter 2027",
      "StartsAtUtc": "2027-01-01T00:00:00Z",
      "EndsAtUtc": "2027-02-01T00:00:00Z"
    }
  ]
}
```

Startup accepts definitions through the durable lifecycle repository before command
registration. Existing definitions can be reloaded after start, but creating or
changing a started season is rejected. Removing a definition from the file does not
delete accepted history. Earlier definitions successfully accepted before a later
configuration conflict remain durable. Invalid season composition disables this
optional module while other modules continue. Restart/reload applies config changes.

The global timer runs independently of connected players and of achievement/gameplay
module enable switches. It copies only rewards that those modules already committed.
Ended but unclosed seasons continue receiving eligible delayed rewards. Per-season
errors are isolated; unload cancels in-flight work and unregisters all owned commands.
Season level curves reuse achievements.json Levels independently of achievement
enablement. There is no automatic balancing, rank-point mutation or prize payout.

| Command | Behavior |
| --- | --- |
| `!anoseason [season]` | Own current season XP/level, or accepted historical ID |
| `!anoseasons [page]` | Five accepted season windows per page, newest first |
| `!anoseasontop [season] [page]` | Five independent ranked SteamIDs per page; defaults to current season |
| `!anocloseseason <season>` | Explicitly freezes an ended season; requires `ano.progression.season.close` |

Read commands require a connected player and suppress output if the session changes
during database access. Toplist placement uses XP descending then SteamID ascending;
SteamIDs identify offline players without requiring a mutable display-name cache.
Closed seasons show `(frozen)` and remain readable by ID. Closure may be dispatched
from the authorized server console. It is idempotent and never closes before the
configured end. Drain gameplay/reward/season checkpoints before closing: late and
unprocessed rewards are deliberately excluded after the durable freeze. The command
does not assert that raw-event or reward backlogs have drained.

Native acceptance: create a future season, restart to accept it, cross its start,
earn gameplay/achievement/challenge XP, verify own season/lifetime separation and
boost metadata, include an offline account, cross the end, drain delayed processing,
close with an authorized admin, then query frozen history and the next season. Check
permission denial, invalid config isolation, pagination, reconnect and unload. Nine
module tests complement the transactional MariaDB tests; native behavior is manual.

## Permanent achievement prerequisites

Each entry in `achievements.json` accepts an optional `Prerequisites` array:

```json
"Prerequisites": [{ "AchievementId": "headshots", "Tier": 2 }]
```

All listed tiers must already be durably unlocked by the same player before any
new tier of the dependent achievement can be awarded. These are permanent gates:
statistic resets do not revoke existing unlocks. Progress still uses lifetime
statistics, including observations made before the prerequisite was unlocked.
An empty/omitted array preserves existing behavior. Explicit null arrays fail
validation. IDs are exact and case-sensitive; references must exist in the catalog
and name a valid tier. At most 32 unique prerequisites are allowed per achievement;
self-references and cycles are rejected before module registration.

The checkpoint processes parents before children, so a committed parent can open
its children in the same pass even when alphabetical IDs would put children first.
The repository checks prerequisites under the same player-account transaction lock
as XP and unlock writes; evaluation candidates never satisfy a gate. No schema
migration or competitive rank mutation is required. `!anoachievements` lists missing
prerequisite tiers as locked. Native chat presentation still requires CS2 acceptance.

## Committed reward events and lifetime level notices

Issue #291 connects all three live reward sources to the shared event bus:

- `ProgressionXpGrantedEvent`: each newly committed gameplay, achievement or challenge grant;
- `ProgressionLevelUpEvent`: the lifetime level crossed by that grant, including multi-level jumps;
- `AchievementUnlockedEvent`: each newly committed permanent tier;
- `ChallengeCompletedEvent`: each newly committed challenge occurrence.

These public contracts live in `AnoCore.Modules.Progression` and use the existing
engine-independent `IAnoEventBus`. Consumers reference the progression assembly
alongside Abstractions. Events retain the immutable committed ledger record and the
captured player session. They never recalculate rewards, update rank points or query
another score store. Lifetime level before the grant is derived from
`LifetimeXpAfter - AwardedXp`; the accepted curve determines both levels.

Live modules publish only the new records returned by persistence. Empty/replayed
results emit nothing. Grant batches are presented in account-revision order within
each source; separate checkpoint sources can interleave, so consumers use grant ID
and revision rather than assuming globally ordered delivery. There is no durable
outbox: observer failure, cancellation, unload or a process crash after commit can
lose an event or notice. Neither event delivery nor a reward is replayed to recover
presentation. Observers must never use these best-effort events as the authoritative
source for financial/reward persistence; the existing ledger remains authoritative.
Observer exceptions are isolated and diagnosed without suppressing later event kinds
or changing committed rewards. Settings failures and delivery failures are also
isolated from persistence.

The live plugin owns one level-notification subscriber for all enabled reward
sources. `progression.level-notifications` defaults to true and appears in the
existing player settings commands/menu. Turning it off only suppresses level chat;
XP and public events continue. Delivery rechecks the captured session after the
asynchronous settings read and pins that session in the native message target.
Reconnect, disconnect and unload suppress stale notices. There is no catch-up of
old levels after reconnect/reload. Activation failure and unload remove the shared
subscription and toggle.

Native acceptance: cross a lifetime threshold through gameplay XP, a challenge
reward and an achievement reward; verify one level transition notice per new grant,
then disable the toggle and repeat. Reconnect during a checkpoint, reload/unload,
and restart with already awarded records; verify no stale or historical notices.
These CS2/DatHost observations remain separate from automated event/service tests.


## Audited lifetime XP administration

Issue #293 adds four commands with separate permissions:

| Command | Permission |
| --- | --- |
| `!anogivexp <target> <amount> <reason>` | `ano.progression.xp.give` |
| `!anotakexp <target> <amount> <reason>` | `ano.progression.xp.take` |
| `!anosetxp <target> <amount> <reason>` | `ano.progression.xp.set` |
| `!anoresetxp <target> <reason>` | `ano.progression.xp.reset` |

Targets use the existing centralized name/SteamID64 resolver, authorization,
self-target and immunity policy. Offline targets require an explicit SteamID64.
The server console is supported. Commands exist when at least one lifetime reward
module is available; shutdown and activation rollback remove them.

Amounts are 0–1,000,000,000 XP per operation; give/take require a positive value.
Taking more than the current lifetime total is rejected. Set/reset affect the current
lifetime balance and its derived account level. They preserve earned-grant history,
permanent achievement tiers, challenge completions, season accounts/history and rank
points. Administrative changes never receive boosts or enter season reconciliation,
and never masquerade as earned-XP or level-up events.

Migration 019 adds `ano_progression_admin_requests`. The persistence API requires a
stable request UUID; an exact retry returns the original result without mutation or
another audit. Reusing a UUID with changed actor, target, operation, amount, reason or
normalized UTC time is rejected. Console/chat command executions each represent a
new deliberate request and generate a fresh UUID shown in the result; repeating the
command creates another operation, unlike retrying an API request with the same UUID.

One MariaDB transaction serializes on the existing progression account row and
commits the lifetime balance/revision, request result and existing shared admin audit.
Audit failure rolls all three back. Earned awards use the same account lock, avoiding
lost XP during concurrent administration. No destructive deletion of grant history
occurs, even on reset. Back up the database before migration; DDL remains retry-safe
rather than falsely treated as transactional.

Native acceptance: verify each command from an authorized player and console,
permission denial, equal/higher immunity and ambiguous targets; use an offline
SteamID64; restart and inspect audit/lifetime state. Confirm season XP, competitive
rank points and permanent unlocks remain intact after lifetime set/reset.


## Progression menu and active boost status

Issue #295 adds `!anoprogression` to the existing native menu bridge. The hub shows
only enabled/registered progression sources and prefers `!anoxp` for lifetime status,
falling back to `!anolevel` when gameplay XP is disabled. Views cover lifetime XP,
permanent achievements, active challenges, current season, accepted season history
and the current season leaderboard. The paged `!anoseasontopcurrent [page]` reuses the
existing season leaderboard implementation and current-season resolver; the explicit
historical `!anoseasontop <season> [page]` remains available.

Selecting a view runs the existing read command as the current player. Menus display
bounded, HTML-escaped command-result rows, with refresh/back and bounded pagination
for paged sources. A rejected page displays the command's validation message and
previous/back navigation. No new database store, XP calculation or gameplay tracking
is introduced. Captured sessions are checked before and after asynchronous reads;
per-player request revisions prevent an older slow response replacing a newer view.
Owned menus disappear on disconnect/unload, and activation failure rolls back the
command/subscription registrations. Administrative commands are never menu sources.

`!anoxp` now shows XP remaining to the next configured level (or the highest-level
state) and the strongest current gameplay XP modifier, including its stable ID and
UTC evaluation time. The same resolver used for earned grants resolves scheduled
and recurring weekend boosts. This current status does not reinterpret historical
awards, whose original event timestamps still govern their multipliers.

Native acceptance: open the menu with combinations of enabled sources, navigate
all views and pages, refresh after earning XP, inspect current/historical seasons,
then reconnect/reload while a view loads. Verify navigation and text in the client,
and check active scheduled/weekend boost boundaries with the server UTC clock.


## Combat-backed challenge counters

Issue #297 adds `CounterSource` to recurring and predefined challenge definitions.
Omitted/zero preserves existing gameplay-stat challenges. The default JSON store
serializes enums numerically:

| CounterSource | Numeric value | Counted facts |
| --- | --- | --- |
| GameplayStat | 0 | Existing `Statistic` gameplay counter |
| CombatKills | 1 | Enemy kills from raw death records |
| CombatAssists | 2 | Valid assists on enemy kills from raw death records |
| UtilityDamage | 3 | Enemy health damage from HE grenades/fire |

For example, set `CounterSource: 1`, `Target: 25` on a recurring weekly challenge
for ordinary kills, or `CounterSource: 3`, `Target: 500` for 500 utility health damage.
`Statistic` remains a required valid gameplay-stat value for compatibility with the
existing definition/evaluation API, but it does not select the count when a nonzero
source is configured. The explicit source selects the raw ledger query. Give new
semantics a new stable challenge ID; changing a source/version must never reset or
repay an already committed occurrence.

All sources share the UTC half-open window and exclude observations later than the
checkpoint time. CombatKills excludes suicides/world kills/teamkills. CombatAssists
also requires a real attacker and excludes victim/attacker self-assists. UtilityDamage
counts `damage_health` for `hegrenade`, `inferno`, `molotov` and `incgrenade`, excluding
team/self damage, other players' damage, gun damage and armor damage. Weapon matching
uses stored normalized names. No second counter or native event hook is introduced.

The existing atomic completion/reward transaction, prerequisite logic, occurrence
identity and replay behavior are reused. Raw ledgers preserve progress across statistics
resets. Existing core death/damage migrations provide these facts; this change requires
no new schema migration. Native acceptance must still verify that the current CS2 host
supplies the expected weapon names and eligible combat events.


## Opt-in challenge progress notices

Issue #299 adds the independent persisted toggle
`progression.challenge-progress-notifications`, disabled by default. Completion
notices keep their existing separate toggle and behavior. Enable progress notices
through the shared settings commands/menu if intermediate updates are desired.

The existing checkpoint's evaluation supplies counts; there are no new database
queries or per-game-event notices. The first observation of each session, challenge
occurrence and definition version is a silent baseline. Only later positive increases
for active incomplete tasks can produce a bounded session-pinned chat notice.
High-water counts suppress repeated/decreased/recovered values. Completed, locked,
expired and ready-to-complete tasks do not produce intermediate notices.

Observed state advances before preference reads or delivery, so enabling a preference,
recovering a failed transport or reloading the plugin does not replay historical
progress. Reconnect, new windows and changed versions start another silent baseline.
The service prunes offline/old-session/expired observations at every checkpoint,
bounds retained observations and clears them on unload. Settings/transport/diagnostic
failures remain isolated from durable progress and rewards.

Native acceptance: opt in, establish a baseline, increase progress before completion
and check the count; repeat a checkpoint, reconnect and disable/re-enable the toggle.
Verify no catch-up or duplicate intermediate notice and unchanged completion rewards.

## Live gameplay XP configuration reload

An active gameplay XP module owns the shared `gameplay-xp` reload registration.
Use the existing permissioned `!anoconfigs` / `!anoreloadconfig gameplay-xp` commands
after editing its JSON configuration. Validated kill/assist/gameplay weights,
batch size and weekend multiplier become visible atomically. `!anoxp` reads the
current active boost policy; a whole reconciliation checkpoint captures one policy
for every player, so a concurrent reload never mixes versions within that checkpoint.

Activation (`Enabled`), `CheckpointSeconds` and `EarnFromUtc` remain fixed until
restart. Changing them through reload is rejected and keeps the previous policy,
as do invalid JSON/weights. XP curves and global scheduled modifiers remain in the
separate achievements configuration and require restart here. Already committed
grants remain immutable/idempotent; still-unprocessed event backlog uses the policy
selected by its next checkpoint. Reload does not recalculate historical XP.

The module releases reload registration on failed command registration and unload.
Disabled modules register no live reload capability; enable them through restart.
Native timer/status acceptance remains a real-server gate.


### Durable challenge predicates (#330)

Recurring templates and predefined definitions accept an optional `Predicates`
object. Existing definitions default to empty filters and keep their behavior.
All populated lists must match (AND); entries within one list are alternatives
(OR). Matching uses exact case-sensitive engine keys, never wildcards. Lists
accept at most 16 unique values and are copied/sorted into immutable snapshots.
Keys must be lowercase ASCII engine keys (`a-z`, digits, `_`, `-`, `/`, `.`),
with at most 128 characters for maps and 64 for weapons. Use `ak47`, not
`weapon_ak47`. Hitgroups are integers from 0 through 255.

| CounterSource | Value | Supported predicates |
| --- | --- | --- |
| GameplayStat | 0 | Maps |
| CombatKills | 1 | Maps, Weapons and native death conditions (see below) |
| CombatAssists | 2 | Maps, Weapons and native death conditions; conditions refer to the killing attacker |
| UtilityDamage | 3 | Maps, Weapons (utility only), Hitgroups |
| DamageHealth | 4 | Maps, Weapons, Hitgroups |

For example, a recurring damage mission can use:

```json
{
  "Id": "weekly.ak-damage",
  "Version": 1,
  "Name": "AK damage on Dust2",
  "WindowKind": 1,
  "Statistic": 15,
  "CounterSource": 4,
  "Target": 1000,
  "RewardXp": 100,
  "PrerequisiteIds": [],
  "Predicates": {
    "Maps": ["de_dust2"],
    "Weapons": ["ak47"],
    "Hitgroups": []
  }
}
```

`Statistic` remains the evaluation slot for non-gameplay counters and does not
change their counted facts. DamageHealth sums the recorded health damage,
excluding self/team damage and events without this player as attacker; it does
not calculate damage again. UtilityDamage retains its existing utility-only
constraint and permits a narrower utility weapon list. Unsupported combinations,
null lists, duplicate/oversized lists, unknown predicate properties and invalid keys fail validation instead
of silently counting unrelated events. No schema migration or new combat write
path is required. Filter values are SQL parameters; string comparisons explicitly
use binary matching regardless of database collation.

Counters read raw committed facts inside the existing UTC half-open window and
as-of timestamp. Buffered damage can appear after its next successful flush;
disabled damage recording supplies no new damage progress. Statistics resets do
not erase challenge evidence. Completion/reward locks and occurrence identity
remain unchanged: replayed events do not count twice, and changing filters,
version or rewards cannot pay an already completed occurrence again. Changes to
an uncompleted occurrence re-evaluate its existing window facts under the new
filters; define a new ID/window if separate progress is intended.

The native death-context extension below enables weapon/map/team and supported
kill conditions for newly recorded facts. Older deaths without context remain
eligible only for unfiltered counters. No historical context is inferred.

Native acceptance: configure one map-filtered gameplay mission and one weapon/
hitgroup damage mission; verify other maps/weapons/hitgroups and team/self damage
remain excluded, matching facts advance after commit, and reconnect/reload/retry
shows one reward only. Run these checks on CS2/DatHost before release.


### Shared server policy in progression.json (#335)

The server now reads shared level thresholds and scheduled XP boosts once from
`config/progression.json` for achievements, challenges, gameplay XP and seasons.
It no longer requires achievement activation/catalog validation to enable the
other progression modules. `achievements.json` remains the permanent achievement
catalog; its legacy Levels/Boosts properties are accepted for compatibility.

On the first startup without progression.json, AnoCore copies the existing
Levels/Boosts from achievements.json into the new file. It leaves the old file
unchanged. Subsequent startups use progression.json exclusively for shared policy.
Invalid existing canonical files fail validation rather than falling back and
silently replacing an operator's settings. This migration does not touch any
account, XP grant, completion or unlock row. Restart is required for shared policy
and definition catalog changes. Changes to curve thresholds can change the level
calculated from unchanged XP, but never recalculate earned XP itself.

```json
{
  "Levels": [
    { "Level": 1, "MinimumXp": 0 },
    { "Level": 2, "MinimumXp": 500 },
    { "Level": 3, "MinimumXp": 1100 }
  ],
  "Boosts": [
    {
      "Id": "halloween-2026",
      "Name": "Halloween Double XP",
      "StartsAtUtc": "2026-10-30T18:00:00+00:00",
      "EndsAtUtc": "2026-11-02T00:00:00+00:00",
      "Multiplier": 2,
      "EligibleSources": 1
    }
  ]
}
```

Name is an optional printable label (1-128 characters); anoxp shows it for the
active scheduled boost. ID remains the durable identity. Windows are UTC [start,
end), at most 128 windows and 500 consecutive levels. Highest eligible multiplier
wins, with existing deterministic ID tie-breaking; weekend policy does not stack.
EligibleSources: gameplay=1, challenge rewards=2, achievement rewards=4 (combine
by addition). Rewards are excluded from gameplay boosts unless explicitly opted
in. Keep all existing EarnFromUtc values when updating gameplay-xp.json; changing
this start can expose historical ungranted facts to reconciliation.

Server content ownership: gameplay-xp.json controls action XP/weekend policy;
challenges.json controls names/targets/rewards/prerequisites/windows/predicates;
achievements.json controls permanent names/statistics/tiers/rewards/prerequisites;
seasons.json controls season names/time windows; ranks.json controls competitive
points/names. All of these are server-owned and do not require a Workshop update.
Panorama XML/CSS/static images still require republishing when changed.

Acceptance on CS2/DatHost: preserve a player's earned XP, restart once to migrate,
verify the new file copied the old curve/boosts, then change one level boundary
and scheduled boost label and restart. Confirm unchanged XP, updated level/label,
one-time rewards, and unchanged competitive rank points without Workshop changes.


### Combined native kill conditions (#330 / #335)

Migration 020 adds an optional death-context ledger keyed by the existing combat
event ID. Native composition snapshots map, weapon, attacker team and the actual
CounterStrikeSharp player_death values once on the game thread. Death and context
commit in the same transaction; conflicting context replays roll back. The old
CombatDeath constructor remains valid, with optional Context added for callers
that have evidence. No automatic backfill occurs. Context-less SDK callers keep
recording unfiltered facts. Explicit same-ID enrichment with genuine evidence is
accepted, but changing an already stored context is rejected. Null context on a
legacy replay does not erase evidence. Foreign keys are deliberately absent to
retain legacy table lifecycle compatibility; queries join only existing deaths.

Native source: [CounterStrikeSharp EventPlayerDeath generated API](https://github.com/roflmuffin/CounterStrikeSharp/blob/main/managed/CounterStrikeSharp.API/Generated/GameEvents/EventPlayerDeath.g.cs).
Distance is already in meters. Finite distances in 0-10000 are stored at four
decimal places; invalid/unavailable distance is NULL, not a guessed zero. Native
penetration counts are bounded to 0-32. No reflection, JSON interpretation,
predicate processing or damage recalculation runs in the gameplay handler.

The Predicates object supports these additional fields for CombatKills (1) and
CombatAssists (2): Headshot, NoScope, ThroughSmoke, AttackerBlind (optional booleans;
false is a real constraint, omitted/null means unrestricted), PenetrationMinimum
(0-32), DistanceMinimumMeters/DistanceMaximumMeters (inclusive 0-10000 bounds, four
decimal places), AttackerTeams ([2] = T, [3] = CT, [2,3] = either). They combine
with Maps and Weapons. Assist conditions describe the killing attacker's kill,
not the assister's weapon/team. Hitgroups remain damage-only. No new conditions
are inferred from present player state. Unknown/misspelled predicate properties
are rejected by JSON parsing; validation errors identify the challenge ID and
Predicates property. The bounded immutable snapshot is compiled at load/start;
only parametrized durable reads evaluate it at checkpoints.

Example in challenges.json / Recurring:

```json
{
  "Id": "weekly.mirage-awp-specialist",
  "Version": 1,
  "Name": "Mirage: Scope optional",
  "WindowKind": 1,
  "Statistic": 16,
  "CounterSource": 1,
  "Target": 5,
  "RewardXp": 250,
  "PrerequisiteIds": [],
  "Predicates": {
    "Maps": ["de_mirage"],
    "Weapons": ["awp"],
    "NoScope": true,
    "ThroughSmoke": true
  }
}
```

For AK headshots use Weapons ["ak47"], Headshot true, Target 10; for wallbangs
use PenetrationMinimum 1. Add AttackerTeams, distance bounds or other booleans
as needed. These are real supported JSON fields; earlier Event/Conditions/Window
sketches were design ideas, not this schema. Use CounterSource, Predicates and
WindowKind as above. Daily/weekly templates use Recurring; dated/season definitions
use Predefined with StartsAtUtc/EndsAtUtc. Keep stable IDs/windows for one-time
rewards; changing an uncompleted rule re-evaluates raw facts, not frozen progress.
Old facts without a context row or with NULL distance do not satisfy requested
context/distance conditions. Unfiltered counters remain backwards compatible.

Acceptance: on CS2/DatHost verify a matching AK headshot, AWP no-scope and combined
smoke/map condition advance; wrong weapon/map/team, unmatched booleans, old context-
less facts, team/self kills and out-of-window events do not. Reconnect/restart and
retry confirm one reward; existing ranks and action points remain unchanged.


### Native fact coverage for server-configured progression (#340)

Native enemy kills now emit the existing GrenadeKill, InfernoKill, ImpactKill,
KnifeKill and TaserKill gameplay facts using the same weapon-family classifier
as live rank scoring. Ordinary gun kills do not invent a weapon-family statistic;
use CombatKills with weapon predicates for those missions. Victim identity enters
the stable stat event ID so retries cannot duplicate progress. Team/self kills
remain excluded. Facts are recorded directly, without replaying the live-rank
weapon bonus.

Bomb pickup/drop and hostage hurt now record gameplay facts through their native
handlers as well as the existing independent rank path. Bomb explosion, other
CT players on defuse and all-hostages-rescued emit eligible connected-team facts;
the defuser is excluded from BombDefusedOthers. Those team objectives are excluded
in FFA. Existing eligibility (warmup/minimum human players) still gates statistics.
The new facts enable configured XP, achievements and challenges; they do not
change configured competitive points. Existing rank team/objective awards retain
their prior single path. No migration or retroactive generation occurs.

PlaytimeInterval is currently a rank-only native policy interval; it is not a
periodic progression gameplay fact. Leave its GameplayXp weight at zero. Actual
playtime totals remain tracked separately. Native acceptance: one knife/taser/
grenade-family enemy kill and one of each supported objective advances configured
statistics/XP once, while live rank receives its existing award exactly once.
