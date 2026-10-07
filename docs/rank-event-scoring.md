# Rank point event ledger

The event ledger is the persistence foundation for independent live rank policy
under #275. It is exposed as `IRankPointEventRepository` through shared runtime
services. This package does not change `ranks.json` or enable native event scoring;
policy and native composition are follow-up work. Existing scoring stays derived
from effective statistics after an upgrade.

## Atomic event identity

`RankPointEventBatch.Create` accepts a stable non-empty event GUID, a bounded ASCII
source, UTC occurrence time and 1–64 unique player awards. Each signed award is
bounded to ±1,000,000 points. Inputs are snapshotted, sorted by SteamID64 and rounded
to the database's microsecond precision. A zero award is valid and preserves event
identity without affecting points.

`ApplyAsync` commits the complete batch in one transaction. The header row serializes
replays of the same event; its SHA-256 payload fingerprint includes source, normalized
time and every player/amount. An identical replay returns `Applied=false`; a changed
payload fails rather than overwriting historical points. Failure on any participant
rolls back the header and all awards, so retry can apply the whole batch. Different
events append independent rows; there is no cached parallel score total. `ReadAsync`
returns a bounded immutable audit snapshot by event identity.

## Single score source

The additive `RankScoreWeights` overload takes `RankScoreSource`. Existing constructors
retain `DerivedStatistics`. `EventLedger` selects only committed rank event amounts:
it does not silently add combat/gameplay-derived points or backfill historical
statistics. Raw score is starting baseline plus the selected source, before the
existing administrative adjustment and zero floor. Placement and toplists combine
baseline, selected source and the audited adjustment before flooring once.

Ties remain ordered by SteamID64, pagination is bounded, and profile-only players
participate when the starting baseline is nonzero. Legacy zero-baseline participant
semantics are preserved in derived mode. An event-only participant remains visible
with zero points after net penalties. Statistics reset cutoffs do not erase rank
ledger events. Administrative rank reset clears the existing adjustment; it does
not delete earned events. A future rank-event reset requires an explicit audited
policy rather than destructive ledger deletion.

Migration 018 creates `ano_rank_point_batches` and `ano_rank_point_events` with
retry-safe DDL. It is registered in runtime startup, independently of the optional
progression migrations 013–017. Rank events never read or write progression XP,
seasons, achievements or challenges.

## Verification

Contract tests cover bounds, snapshots, normalized timestamps and legacy constructor
compatibility. MariaDB tests cover restart/replay, concurrent same/different events,
conflicting payload rejection, full-batch rollback and retry, zero awards, migration
reapplication, source isolation, adjustments, flooring, baselines, ordering,
pagination and statistic-reset independence. Live eligibility, dynamic/VIP policy,
streak bonuses, summaries and native scoreboard composition remain #275/#228.
