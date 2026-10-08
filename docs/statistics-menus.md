# Personal Stats and server-wide Stats

The home navigation separates **Personal Stats** from **Stats**. The compact
personal view reads only the invoking player's persisted totals. Stats is a
category selector followed by server-wide rankings, including offline players.
The player's own row is marked `(you)` when present; rows contain placement,
stored display name and value. Names are bounded, stripped of control characters
and escaped for native presentation.

| Command | View |
| --- | --- |
| `!anopersonalstatsmenu` | Own playtime, K/D/A and K/D, native HS%, match W/L and win%, round wins/MVP, and current rank points/placement when ranks are enabled |
| `!anostatsmenu` | Server-wide category selector and paginated rankings |
| `!anostatdetails [map] [weapon]` | Existing own detail/hitgroup/gameplay view, retaining map/weapon filters |

The old filtered personal `anostatsmenu [map] [weapon]` view moves to
anostatdetails; anostatsmenu now means the server-wide selector. Direct anokda,
anodetailstats, anohitgroups, anogamestats and existing rank commands still read
their current ledgers. The dashboard remains the own overview defined by #322.

## Categories and sample requirements

| Category | Source and minimum sample |
| --- | --- |
| Playtime | Existing persisted session durations, including accounted online time |
| Kills / Deaths / Assists | Existing combat ranking queries and effective reset views |
| K/D | At least 50 kills and 20 deaths; players with zero deaths do not divide by zero |
| HS% | At least 50 valid kills with persisted native death context; headshot count and denominator use the same cohort |
| Match wins / Match win% | Effective MatchWon/MatchLost counters; percentage requires at least ten recorded match outcomes |
| Round wins / MVP | Effective gameplay counters |
| Bomb plants / Bomb defuses | Effective gameplay counters |
| Grenade kills / Knife kills | Effective gameplay counters |
| Rank points | Enabled rank module's existing score queries, source, baseline, weights, adjustments and floor |

The rate minimums prevent a single event from leading the leaderboard. They are
displayed with the selected category. A personal rate can show a smaller sample;
no known denominator displays a dash. Personal HS% also shows its native-kill
sample. Historical combat deaths without native context are **unknown**, never
silently assumed to be body kills. Consequently this HS% can differ from the old
dashboard's separate gameplay-counter ratio. No historical data is backfilled and
no progression, bonus or rank-point rule changes.

## Persistence and presentation

Rankings order by value descending, then SteamID ascending. Placement is the
one-based position in that deterministic order, matching existing ranking
commands. SQL joins stored profile names once and never loads profiles separately
for each row. Five rows plus one look-ahead row determine whether Next exists;
Previous exists only after the first page. Database readers bound limits to 100
and offsets to 10,000; the menu further bounds navigation to page 1000.

Gameplay category/rate queries reuse effective reset views. Migration **021** adds
a covering gameplay index beginning with stat kind and player identity, including
event time and amount for reset-aware aggregation. It checks an existing index's
definition and can restart after index creation without duplicating it. Existing
data and configuration remain intact. Initial index creation on a large existing
ledger can take time; this change has not been installed on the live database.

Both presenters use the same definitions. Titles remain Stats or Personal Stats;
the additive SuppressPageIndicator property prevents a redundant/misleading
Panorama page count while preserving actual navigation. Existing menu constructors
and default page presentation remain compatible. Reconnect, unload, closure,
newer requests and a different current view reject obsolete results/selections.

IStatisticsMenuRepository is an additive optional capability; existing
IGameplayStatRepository and ICombatRepository implementations gain no required
members. The standard MySQL repository exposes it and GameplayStatsModule owns
the menu lifecycle. Third-party repositories without that capability keep the
filtered detail command but do not advertise unavailable server-wide menus.

## Verification

Automated menu tests cover separation, empty/single/multiple pages, offline names,
requester markers, exact configured rank source/weights, escaped names, stale
selection, closure/reconnect/unload, and page-indicator/navigation behavior.
Database integration covers rates/minimums, stable ties/placement, unknown native
context/team-kill exclusion, all gameplay categories, effective resets, existing
count/playtime queries, empty results and restart-safe index creation.

Native CS2/DatHost tests remain manually skipped. Later verify both menu presenters,
offline profiles, previous/next/categories, reconnect/close during an async query,
and agreement of Rank points with anorank under the deployed backup configuration.
