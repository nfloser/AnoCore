# AnoCore roadmap

## Foundation

- Core/framework and module lifecycle
- Player system
- MySQL/MariaDB persistence and migrations
- Roles/permissions
- Command system using the `!ano...` convention for Ano-specific commands
- Config and player settings
- Menu/UI foundation
- Messaging, events, placeholders and localization
- Logging, security and developer SDK

## Community modules

- Chat/tags
- Rank system
- Statistics
- Playtime
- Toplists
- Admin tools

## Progression and seasons

- Lifetime XP and account levels independent from competitive rank points
- Season XP/levels with preserved historical results
- Permanent and tiered achievements
- Daily, weekly and season-long challenges backed by existing AnoCore events/stats
- Predefined challenge catalogs for each season
- Scheduled XP events such as double-XP weekends
- Progression menus, notifications and deterministic leaderboards
- Tracked in #229

## Player rating and scouting

- Internal `AnoRating` derived from existing gameplay statistics, never rank points
- Explicit provisional/confidence state for new or low-sample players
- `!anorating` for current-player comparison and manual team composition
- No automatic team balancing
- Optional live Leetify context, displayed separately and never folded into AnoRating
- No FACEIT integration
- Internal versioned list/detail commands implemented in #234; opt-in live Leetify context implemented in #242, native/provider acceptance remains #230
- Tracked in #230

## Maps and voting

- Reusable vote engine
- Central map registry with Workshop IDs
- Map history/random selection/mapvote
- `!anoveto` and vote popup
- one vote per player
- automatic winning-map load

## Tournament/match

- Teams and forced tournament team placement
- reconnect-safe assignments
- ready system
- knife round and side choice
- BO1/BO3/BO5
- veto integration
- overtime and pauses
- match history/results/stats
- match-state recovery

## Platform

- REST APIs
- dashboard
- server management
- CI/release automation
- tested migration from existing server data
