# Combat performance work

Tracking: #329. This work preserves the engine-reported damage and existing
stat/rank/progression semantics. It does not recalculate CS2 damage.

## GameRules lookup

Statistics eligibility and live rank scoring share a validated `cs_gamerules`
proxy reference. Each call still reads the current `WarmupPeriod`; the boolean
is never cached. A valid reference avoids repeated entity enumeration even when
many shots/hits occur within one tick. An invalid reference is replaced. Missing
or failed reads are retried next tick, with at most one failed lookup per tick.
Map start, map end and plugin unload discard the reference and the retry marker.
All native access stays on the game thread. Existing missing-state defaults are
preserved: ranks assume warmup, gameplay statistics assume non-warmup.

The unit load fixture reads 10,001 times, changes warmup in the same tick and
requires only one entity lookup while the entity remains valid. It establishes
lookup reduction, not measured native frame-time improvement.

## Native acceptance

On a disposable CS2/DatHost server:

1. Enable the existing statistics/rank policies and connect two human clients.
2. Shoot and deal damage during warmup, transition to live play, then repeat.
   Verify configured warmup eligibility and rank exclusions still apply.
3. Change maps twice, including the same map, and repeat. Hot-reload/unload and
   load the plugin during warmup and live play; inspect native-read errors.
4. Run a ten-player combat scenario. Compare game-thread profiling and logs to
   the previous build under the same workload. Record map, tick rate, duration,
   event counts and frame-time distribution; do not infer speed from unit tests.

## Remaining packages

- Bound and batch combat-detail writes; explicit recording settings, retry and
  backpressure; disconnect/map-end/shutdown flush and consistency for reads.
- Profile event-identity hashing before altering replay/idempotency semantics.
- Database outage/recovery and ten-player write-volume/load verification.

Related new features: #330 challenge predicates, #331 Workshop previews,
#332 default-hidden battlepass. They remain separate implementation issues.
