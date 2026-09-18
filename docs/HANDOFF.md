# AnoCore development handoff

## Current workstream

- Full functional scope remains in `docs/functional-acceptance.md`; umbrella #11 remains open.
- AnoVeto issue #20 / PR #31 is merged into `main` at `4848394f76fbffc1ec10455f6259002fe57b055c`.
- Active prerequisite work for #17: issue #37 / PR #38 / branch `feature/37-targeting-immunity`.
- Extended administration commands #36 remain a separate workstream; do not overwrite or duplicate that scope.

## Known-good targeting checkpoint

- Reviewed implementation head before this documentation checkpoint: `a83157dad57caed470a447d6c68e9b26c1982a59`.
- CI run: `35227485297` (#115).
- Release build: passed with zero warnings and zero errors.
- Test suite: 134/134 passed, including MariaDB integration and the new targeting/runtime-composition tests.
- Formatting: passed.
- Development plugin publish: passed.
- Deployment package validation: passed.
- Artifact upload: passed.

## Implemented in issue #37 / PR #38

- New CounterStrikeSharp-independent `IPlayerTargetResolver` contract.
- Online human targets resolve by exact SteamID64, exact case-insensitive name or unique case-insensitive name prefix.
- Ambiguous prefixes/names are rejected instead of choosing a best guess.
- Special selectors `@me`, `@all`, `@t`, `@ct` and `@spec` are capability-gated per consuming command.
- Multi-target results use deterministic SteamID ordering.
- New `ITargetAuthorizationService` centralizes operation permission, self-target policy, online-state checks, reconnect/stale-session rejection and existing immunity enforcement.
- Immunity is not duplicated: target authorization delegates to the existing `IAuthorizationService.CanTargetAsync`.
- `RuntimeServices` exposes exactly one shared resolver and one shared target-authorization service for future admin/moderation/extended-command modules.
- Tests cover SteamID/name/prefix resolution, ambiguity, selector capability restrictions, team/all ordering, permission denial, self policy, missing players, stale reconnect sessions, equal/higher immunity blocking and shared runtime composition.

## Review status

- Critical paths reviewed: public targeting contracts, resolver ambiguity behavior, selector capability boundaries, target authorization ordering, reconnect safety, immunity delegation and runtime composition.
- No remaining code-review blocker is known on the implementation head.
- This feature intentionally does not implement kick/ban/mute/gag or engine mutations; those remain later #17 packages.
- Native CS2 acceptance is not required for this pure runtime prerequisite, but all eventual engine-changing admin commands still require real-server acceptance before production-release claims.

## Next steps

1. Require final CI on the documentation checkpoint head, then mark PR #38 ready and merge it only if the exact head remains green and mergeable.
2. Continue #17 from the merged targeting foundation: moderation/admin primitives and commands should consume `IPlayerTargetResolver` and `ITargetAuthorizationService` rather than reimplementing target or immunity logic.
3. Keep #36 Extended Commands separate until the #17 targeting/admin foundations it depends on are merged.
4. Preserve small test-first commits, CI gates, self-review and this handoff before context boundaries.

## Integration rules

Use the shared services in `RuntimeServices`. Do not create a second player registry, permission evaluator, target resolver, target-authorization layer, database, command registry, menu service or vote service. Commands that accept multi-target selectors must explicitly opt into the relevant selector capabilities. Engine calls after asynchronous work must be marshalled onto the server update thread.

License, NOTICE and source provenance must remain intact.
