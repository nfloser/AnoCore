# AnoCore Development Handoff

This file is updated before major context or work-session boundaries so another development session can continue without guessing.

## Current workstream

- Issue: #12 — Implement event bus and CounterStrikeSharp runtime composition
- Branch: `feature/12-runtime-event-bus`
- Pull request: #24 — Implement event bus and CounterStrikeSharp runtime composition
- Latest tested implementation commit: `d21d452bc57aa906cbcf3ef174b6a8ab59dcfcf4`

## Implemented

- Thread-safe in-process `AnoEventBus` implementing `IAnoEventBus`.
- Registration-order delivery and event-type isolation.
- Independent, idempotent subscription disposal.
- Subscriber failure isolation followed by aggregated error reporting.
- Cancellation before and between subscriber deliveries.
- Snapshot semantics when subscriptions change during publication.
- CounterStrikeSharp player connect/disconnect/team/spawn/death hooks wired to the reconnect-safe player registry.
- Disconnect uses the event `Xuid`, so cleanup does not depend on a still-valid controller.
- Team/spawn/death refresh on the next server frame.
- Hot reload bootstraps already connected human players into the fresh registry.
- Unload explicitly deregisters all AnoCore lifecycle hooks.
- Async lifecycle failures are observed and logged.
- Real CS2 server verification procedure documented in `docs/runtime-verification.md`.

## Test status

- Event-bus tests were committed before implementation in `af8dd6fcdc0f6566e428f96d7f92bd3089603c3b`.
- Event-bus implementation commit: `91116c4287391cc9051736be53abe74cb0b7bfee`.
- CSS runtime wiring commit: `4bb6089816528b584ee8bbb61fdeb502cf7c249f`.
- Verification documentation commit: `d21d452bc57aa906cbcf3ef174b6a8ab59dcfcf4`.
- PR #24 CI run 19 completed successfully: restore, Release build, all tests and format verification passed.

## Review status

- Event delivery ordering, failure semantics, cancellation and unsubscribe behavior reviewed.
- CSS types remain isolated to `AnoCore.Plugin`; core/runtime remain independent.
- Hot-reload and unload paths explicitly manage handler lifetime.
- No blocking review finding is currently known.
- Native CS2 behavior is not falsely claimed as tested; the documented real-server checklist remains a release gate.

## Open items / next steps

1. Let CI validate this handoff-only commit, submit the PR #24 self-review and merge #12.
2. Start the two independent workstreams #13 (configuration/localization/placeholders/logging) and #14 (MySQL/MariaDB persistence/migrations) from the new green `main`.
3. Keep #3 isolated on `chore/3-canonical-gpl-license`; finish it before public distribution.
4. After #14, continue #15 permissions/roles/immunity; after #15/#14/#12, continue #16 commands/UI/settings.
5. Continue umbrella roadmap #11 through #23; release remains blocked on real-server end-to-end verification.

## Project workstreams

See `docs/workstreams.md` and umbrella issue #11 for dependency ordering and parallelizable streams.
