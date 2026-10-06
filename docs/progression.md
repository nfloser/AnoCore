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

This package contains definitions and deterministic calculation only. It does not
yet persist player XP, grant rewards, define seasons/challenges/achievements, add
player commands or compose native CS2 behavior. Those are separate reviewable
packages under #229.
