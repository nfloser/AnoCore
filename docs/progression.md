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

The definition layer remains engine-independent. Durable lifetime XP is added by
the separate persistence package described below; seasons, challenge/achievement
evaluation, player commands and native CS2 composition remain separate packages
under #229.


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
