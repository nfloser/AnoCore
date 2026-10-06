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
Generic gameplay XP, combat-only achievement metrics, unlock notifications, richer
menus, challenge/season presentation and leaderboards remain separate #229 work.
Native CS2/DatHost behavior has not been verified by the automated build/test gate.
