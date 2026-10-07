# Rank point event ledger

The event ledger is the persistence foundation for independent live rank policy
under #275. It is exposed as `IRankPointEventRepository` through shared runtime
services. The foundation introduced by #278 preserves existing scoring; #280 adds
explicit opt-in native policy composition as documented below. Existing scoring stays derived
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
pagination and statistic-reset independence. Live eligibility, dynamic/VIP policy and streak bonuses are added under #280.
Summaries, playtime awards and native scoreboard presentation remain #228.

## Opt-in live scoring

Live hooks can now select the ledger by setting `"Source": "EventLedger"` in
`config/ranks.json` and restarting/reloading the plugin. Omitted Source stays
`DerivedStatistics`; switching is explicit and never imports old statistics.
Switching back reads the existing effective statistic model again. Keep this
choice stable during a competitive season because the two sources can differ.

`KillPoints`, `AssistPoints`, `DeathPenalty`, `GameplayPoints` and `StartingPoints`
remain shared configuration. `LivePolicy` has these optional controls:

| Setting | Default / bounds | Behavior |
| --- | --- | --- |
| WarmupPoints | false | Independent from statistics warmup policy; unknown warmup state fails closed. |
| MinimumPlayers | 4 / 1–64 | Connected humans on T/CT count; bots and spectators do not satisfy the threshold. |
| IncludeBots | false | Allow human rewards/death penalties in bot interactions; never create bot accounts. |
| FreeForAll | false | Same-team kills count as normal kills; team round-win/loss rewards are suppressed. |
| TeamKillPenalty / SuicidePenalty | 2 / 1; each 0–1000 | Explicit signed deductions; no assist or kill bonus for a teamkill. World deaths use the suicide penalty. |
| WeaponPoints | empty; ≤128 keys, ±1000 each | Exact engine weapon tokens, case insensitive, such as ak47. |
| DistanceThresholdMeters / DistanceBonus | 0 / 0; 0–10000 m, 0–1000 points | Award distance bonus when threshold >0 and kill distance meets it. |
| DynamicMultipliers | false | Kill rewards use victim/attacker point ratio; death penalties use attacker/victim ratio. Each operand is at least one. |
| MinimumDynamicMultiplier / MaximumDynamicMultiplier | 0.25 / 4; positive ordered range ≤4 | Clamp ratios; truncate final signed result toward zero. |
| VipMultiplier / VipPermission | 1 / ano.ranks.vip; multiplier 1–10 | Permission-gated positive awards only; penalties are never VIP multiplied. Permission failures use base awards. |
| StreakWindowSeconds / StreakPoints | 30 / empty; 1–600 s, counts 2–64, bonus 1–1000 | Consecutive eligible kills separated by strictly less than the window; each configured exact count awards its bonus once. |

Death batches combine attacker/victim/valid assister rewards atomically. Supported
special bonuses reuse GameplayPoints for first blood, headshot, no-scope,
penetration, smoke, blind, domination, revenge and flash assist. First blood is
owned by the independent rank checkpoint, rather than the statistics policy.
Round, match, grenade, bomb, hostage and MVP hooks use the same independent rank
eligibility and selected score source. No duplicate statistics counters are created.

A single live rank service serializes policy decisions per server instance. It
checks the committed event before reading dynamic scores or advancing streaks;
failed writes do not advance first-blood/streak state. Dynamic ratios use the
committed score snapshot seen before that event. Simultaneous external-server
writes may change scores later; the stored awarded points remain authoritative.
Streaks reset on victim death, timeout, round change or attacker reconnect and
start fresh after plugin/server restart. Restart does not replay persisted awards.

After commit, warmed chat/rank refresh and threshold notices are best effort and
cannot make a committed rank write fail. Notifications retain the captured player
session through asynchronous preference lookup and native world-update delivery;
old-session notices are suppressed after reconnect. `rank.notifications` still
controls threshold notices. Native scoring/rendering requires the CS2 acceptance
pass. Playtime awards, individual point notices, round summaries and scoreboard
presentation remain separate follow-ups under #228.
