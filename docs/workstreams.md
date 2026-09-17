# AnoCore Workstreams

AnoCore is developed as a set of small, reviewable workstreams. Each workstream uses its own issue and branch and follows the repository lifecycle in `AGENTS.md`.

## Dependency order

1. Core contracts and module lifecycle — complete in #1.
2. Player lifecycle — #4.
3. Event bus and CounterStrikeSharp runtime integration.
4. Configuration, localization, placeholders and logging.
5. Persistence and migrations.
6. Roles, permissions and immunity.
7. Command system, menus and player settings.
8. Admin, chat/tags and messaging.
9. Statistics, ranks, playtime and toplists.
10. Maps, generic voting and AnoVeto.
11. Tournament/match orchestration, including forced team placement in tournament mode.
12. Web/API, server management, security hardening and developer SDK.
13. Existing-server data migration completion, release packaging and end-to-end CS2 verification.

## Parallelizable work

Once the event bus/runtime boundary is stable, configuration/localization/logging and persistence can progress independently. Statistics can progress in parallel with map/vote infrastructure after persistence and player lifecycle are stable. Documentation, licensing and release metadata are maintained independently and must not block functional work unless required for distribution.

## Completion rule

A workstream is complete only when implementation, tests, documentation, CI and review are complete. Runtime-facing work additionally requires a documented CS2 server verification path; release readiness requires the real-server acceptance suite to pass.
