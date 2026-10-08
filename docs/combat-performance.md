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

The unit load fixture reads 10,002 times, changes warmup in the same tick and
requires only one entity lookup while the entity remains valid. It establishes
lookup reduction, not measured native frame-time improvement.

## Native acceptance

On a disposable CS2/DatHost server:

1. Enable the existing statistics/rank policies, set their minimum-player policy
   to allow the test population, and connect two human clients.
2. Shoot and deal damage during warmup, transition to live play, then repeat.
   Verify configured warmup eligibility and rank exclusions still apply.
3. Change maps twice, including the same map, and repeat. Hot-reload/unload and
   load the plugin during warmup and live play; inspect native-read errors.
4. Run a ten-player combat scenario. Compare game-thread profiling and logs to
   the previous build under the same workload. Record map, tick rate, duration,
   event counts and frame-time distribution; do not infer speed from unit tests.

## Batched combat detail writes

`config/combat-recording.json` is created at startup with these defaults:

```json
{
  "RecordWeaponFire": true,
  "RecordDamage": true,
  "BatchWrites": true,
  "Capacity": 8192,
  "BatchSize": 256,
  "FlushIntervalSeconds": 1,
  "ShutdownTimeoutSeconds": 5
}
```

Changes require a plugin/server restart. Existing stats remain intact. Setting
one recording flag to false stops new events of that type, including the event-ID
work in its native hook; damage is still calculated by CS2. These settings affect
shot/hit detail data and consumers of those data; kill/death/assist ledgers,
competitive rank scoring and independent progression are unchanged.
`BatchWrites: false` retains immediate per-event writes. Invalid recording config
is reported and disables only new shot/hit detail recording; core services still
start.

In batch mode, native shot/hit hooks enqueue immutable snapshots without issuing
SQL or creating one asynchronous database operation per event. A background
worker flushes periodically. Each package contains at most 256 original events
and uses one atomic transaction with a bulk insert and bulk replay verification
for each populated table (at most four commands for a mixed batch). It does not
replace the durable event ledger with lossy aggregate counters. Original event
IDs, payload checks, timestamps and map/weapon/hitgroup/filter semantics remain;
replay timestamps may differ as in the legacy single-event API. No schema
migration or Workshop asset update is needed.

Disconnect and map end request an immediate flush of the accepted prefix. New
map events retain their own map metadata; the queue is not erased on map change.
Unload stops new admissions and attempts asynchronous draining with the configured
deadline. The repository operation receives cancellation. CS2 process termination
cannot guarantee an asynchronous unload flush completes.

The dedicated `anodetailstats` and `anohitgroups` commands flush their accepted
prefix before querying and fail if that flush fails. Other API/menu/rating
consumers read committed detail snapshots and can lag until the next successful
flush. Existing statistics resets still filter events by original occurrence time.

Failures keep the accepted prefix, including an in-flight batch, in the bounded
queue for periodic retry. Lost commit acknowledgments can safely replay the same
IDs. Flushes serialize; data accepted after a flush began do not prolong it
indefinitely. There is no persistent local spool: a crash, hard stop, or database
outage beyond the unload deadline can lose uncommitted buffered data. If capacity
is exhausted, new detail events are rejected rather than blocking the game thread
or evicting accepted data. `css_anostatus` shows pending/rejected counts; overload
logs are bounded to powers-of-two rejection counts. Inspect capacity and database
health when this occurs. A conflicting stored event ID prevents that batch from
committing and is reported; investigate instead of silently discarding records.

The engine-independent ten-player fixture produces 10,000 accepted shot events
in twenty 500-event windows and verifies forty packages at the default batch
size, versus 10,000 immediate transactions. The MariaDB fixture verifies a full
256-event mixed package uses one transaction, rollback across both tables,
concurrent replay, restart and recovery from lost commit acknowledgment. These
are transaction-volume/semantic checks, not a measured native FPS improvement.

Additional disposable-server acceptance:

1. Set eligibility to allow the test population, fire and damage through multiple
   maps, disconnect, and verify the accepted tail becomes visible after flushing.
2. Test each recording flag and immediate-write fallback, restart and invalid
   configuration. Existing rank/progression behavior must remain intact.
3. Interrupt MariaDB briefly, inspect retained pending counts, restore it and
   verify replay does not inflate shots/hits/damage. Test capacity exhaustion
   separately with an intentionally small buffer and verify rejection reporting.
4. Hot-unload/reload with queued data and with MariaDB unavailable. Inspect the
   shutdown deadline/error and remaining-count reporting; do not claim crash
   durability. Complete ten-player native profiling from the checklist above.

## Remaining performance work

- Profile event-identity hashing before altering replay/idempotency semantics.
- Record native ten-player frame-time and database transaction-volume acceptance.

Related feature issues: #330 challenge predicates and #331 Workshop previews.
The owner cancelled #332 (battlepass); it is not planned or part of this scope.
