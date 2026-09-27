# Functional acceptance matrix

The target is the complete reference framework and official-module feature set, plus Ano-specific modules. A build, a shared service, or a menu mockup does not satisfy a gameplay feature. This matrix is an acceptance checklist, not a claim of completion.

The source inventory was checked on 2026-09-17 against the reference recorded in NOTICE.md: its core API and official custom-tags, extended-commands, ranks, statistics, time-stats, toplists and administration modules. Provenance remains in NOTICE.md and Git history.

| Area | Required behavior | Current acceptance state / owner |
| --- | --- | --- |
| Core lifecycle | Module registration, startup ordering, rollback, unload, public service contracts | Runtime services and lifecycle exist; external module discovery/API integration remains #22 |
| Player data | SteamID, online/offline reads, storage/settings defaults, save/load/reset, reconnect and safe shutdown | Profile and typed settings persistence connected in #34; registration/reset/batching still required |
| Configuration | Module registration, typed validation, defaults/schema upgrades, reload, shared access | Atomic JSON store exists; module metadata/upgrades and complete reload remain |
| Database | MariaDB/MySQL, migrations, durable writes, backup/recoverable migration, purge | Core plus moderation migrations/repositories are durable and transaction-tested; operational backup/purge acceptance remains #23 |
| Authorization | Roles/groups, inheritance, deny/allow, immunity, command overrides, timed assignments/VIP | Evaluation/persistence exist; #37/PR #38 centralizes permission + immunity + stale-session target authorization; administration and timed assignment enforcement remain #17 |
| Commands | Registered names/aliases, usage/help, permissions, client/server constraints, target resolution | Core registry/bridge and #37 target resolution are connected; #43/PR #44 adds engine-independent ban/unban/mute/unmute/gag/ungag/silence/unsilence registrations; native plugin composition and additional module commands remain #17 |
| Settings UI | Automatically expose module settings, per-player toggles, defaults/reset, persistence | Typed storage exists; complete UI/registration remains |
| Messaging | Player/team/all output, localization, placeholders, center message duration/priority | Individual services exist; engine messaging and priority lifecycle remain #17 |
| Chat and tags | Chat processing, clan/name tags, name/chat colors, ownership priority, permission-based choices and removal | Native persisted Chat restriction enforcement is implemented in #59/#60 and persisted Voice restriction enforcement is implemented in #61/#62; #112 adds validated engine-independent public/team formatting with safe placeholder ordering; #114 adds session-safe warmed synchronous formatting snapshots. Native tag/color broadcast, ownership priority and full chat processor behavior remain #17 |
| Statistics | Global/map/weapon stats; kills/deaths/assists, shots/hits/hitgroups, grenades, objectives, MVP, rounds/matches; warmup/bot/min-player/FFA policies; reset and menus | #18; #78 adds durable kills/deaths/assists and own `anokda` as a subset; policies, detailed counters, reset and menus remain |
| Playtime | Total/today, team/alive-state breakdown, notification settings, reconnect/day rollover and restart persistence | #18; session ledger/checkpoints in #74 are a foundation; team/alive breakdown, menus and notifications remain open |
| Ranks | Configurable thresholds/points and scoring rules; rank menus; give/take/set/reset; rank/tag display and notifications | #18; #86–#107 cover combat-derived ranks, deterministic placement/top list, audited point administration and transition notifications. #108 adds shared-menu rank/leaderboard navigation; #110 adds configured shared rank-tag/name/points placeholders. Native chat/clan application, further scoring rules and live acceptance remain. |
| Toplists | Rank/time/stat queries, deterministic ordering, menu navigation and placement tags | #18; bounded total-time leaderboard in #76 and kill/death/assist leaderboards in #80/#84 are subsets; rank-score queries are added by #88; other statistics, menus and tags remain open |
| Moderation | Kick/silent kick; ban/unban; mute/gag/silence and reversal; warnings; duration/expiry; reasons; offline/server-scoped data; admin menus/audit | Durable sanctions/audit (#41/#42), permissioned commands (#43/#44), connect-ban policy/native disconnect (#45-#48), live command composition (#51/#52), cached snapshots (#53/#54), synchronous communication policy (#55/#56), lifecycle warming (#57/#58) native chat-gag interception (#59/#60) and native voice-mute enforcement (#61/#62) are implemented; kick, warnings and admin UI remain #17 |
| Admin integration | Permission/group administration, immunity, connection information, webhook notifications, compatibility policy | Shared target/immunity is #37/PR #38, durable moderation is #41/PR #42, commands are #43/PR #44, connect-ban policy is #45/PR #46 and its native adapter is #47/PR #48; plugin composition, role-management UI, connection info and webhooks remain #17/#22 |
| Extended commands | Health/armor; freeze/unfreeze; noclip; slay; rename; respawn/revive; strip/give; teleport; speed; bury/unbury; slap; blind/unblind; god; team/swap; hide; protected cvar/server-command controls; same-IP inspection | #36 remains separate; it must consume centralized targeting/permissions/immunity and still needs command behavior + real-server acceptance |
| SDK/events | Player loaded/unloaded, storage reset, settings changed, chat/core-unload events; module-owned cleanup and compatibility | Existing contracts cover part of this; full SDK acceptance remains #22 |
| External integration | Documented data/API access and compatibility requirements for existing administration panels | #22; no drop-in schema compatibility claim |
| Ano maps/vote | Catalog, generic voting, eight-map custom vote, roles, single vote, timeout/runoff/reconnect and real map load | #19 foundations and #20/PR #31 implementation are merged; real CenterHtml interaction and real map transition remain release acceptance gates |
| Ano tournament | Teams, forced placement, reconnect, ready/knife/side choice, BO formats, pauses/overtime/demo/backup and recovery | #21; not implemented as a complete feature |
| Deployment | Full dependency artifact, configuration, upgrade/rollback, diagnostics | Development package exists; real-server acceptance remains #23 |

## Completion evidence

For each row: record the implementing PRs, executable regression/integration tests, configuration and commands, permission-denial checks, real-client observations and restart/unload behavior. Mark a row complete only after its documented behavior is exercised end to end.

Changing a command prefix to `ano` is allowed; silently omitting behavior is not. Preserve upstream attribution where code is adapted. Keep reference comparisons out of product-facing messages.

## Release gate

All required rows must pass. Native CS2/CounterStrikeSharp behavior cannot be established by the unit-test suite. Record host version, installed commit, database version, test accounts and results in #23 before publishing a production release.
