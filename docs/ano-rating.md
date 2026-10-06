# Internal AnoRating

`!anorating` displays connected human players, sorted by internal score descending and SteamID64 ascending on ties. Each page contains five players; use `!anorating list 2` for another page (1–26). `!anorating <name-or-SteamID64>` displays one player's dimensions and observed sample counts. Exact names take priority over partial names; ambiguous selectors require an explicit SteamID64. Console inspection is supported. Reads preserve caller and target sessions and reject stale detail results after reconnects.

The native Stats composition registers this read-only command when combat and gameplay statistics are available. It has no separate event ledger, persisted rating, competitive-rank dependency, progression dependency, automatic team assignment, or external network dependency. Unload removes the command. Ratings use the existing effective statistics, including reset cutoffs, and are recomputed on demand. A command compares query-time totals; concurrent event ingestion can advance totals between component reads.

## Formula `ano-rating-v1`

This is a transparent observational heuristic for manual team composition, not a calibrated win-probability model. Dimensions use values from 0 to 100:

| Dimension | Formula | Weight | Availability |
| --- | --- | --- | --- |
| Combat | With `r = (kills + 0.5 × assists + 10) / (deaths + 10)`, `100 × r / (1 + r)` | 40% | At least one kill, death or assist |
| Impact | Health damage divided by played rounds, capped at 100 | 25% | Played rounds and recorded shots or hits |
| Objective | `100 × (plants + defuses + hostage rescues) / played rounds`, capped at 100 | 10% | Played rounds |
| Utility | `100 × flash assists / played rounds`, capped at 100 | 10% | Played rounds |
| Consistency | `100 × rounds won / (rounds won + rounds lost)` | 15% | Recorded outcomes, no more outcomes than played rounds |

The score is the weighted mean of available dimensions, rounded to a whole dimension point and multiplied by 10, giving a 0–1000 scale in steps of 10. Missing dimensions are omitted and remaining weights are normalized. Objective/utility zeroes indicate no recorded successful events; disabled or incomplete instrumentation cannot be distinguished from actual zeroes. Outcomes reflect team results and are only a coarse consistency proxy. Map, opponent skill, role and player circumstances are not normalized. Versioned formulas make later recalculation explicit.

Confidence describes observed sample volume and coverage, not statistical certainty about skill:

| State | Requirements |
| --- | --- |
| Provisional | Fewer than 20 played rounds or fewer than 20 kills plus deaths; shown as **unscored** |
| Low | At least 20 rounds and 20 kills plus deaths |
| Medium | At least 100 rounds, 100 kills plus deaths and four available dimensions |
| High | At least 500 rounds, 500 kills plus deaths, all five dimensions and 500 shots |

The highest satisfied state wins. New players are never presented with a precise score. Use the detail view and sample counts before making manual team decisions.

## Selected follow-ups

#230 remains open for optional live Leetify context. That provider is not implemented or enabled by this package. Its original metrics must remain separately attributed and must not enter the internal formula. FACEIT and automatic balancing remain outside the selected scope. Independent lifetime/season progression, achievements and challenges remain tracked under #229.

## Validation

Automated coverage includes fixed formula fixtures, missing dimensions, provisional and confidence boundaries, invalid counts, connected-player ordering, pagination, exact/ambiguous targeting, caller/target reconnect suppression and command disposal. Repository fakes reject event writes and rank-point queries.

Disposable-server acceptance: with two human accounts, check list/detail delivery, a new player's unscored provisional marker, populated-stat estimates, pagination, reconnect suppression, statistics reset, and unload/reload command registration. Native display acceptance remains separate from CI.
