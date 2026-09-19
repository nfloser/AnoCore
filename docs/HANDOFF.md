# AnoCore development handoff

## Current workstreams

- Full functional scope remains in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Targeting/immunity #37 / PR #38 is merged in `main` at `ad73aa7cfea8ab4342e599cca8b65146e163551b`.
- Persistent moderation state #41 / PR #42 is merged in `main` at `fd20c543ece4a95f8d1978a1a5a97bb40dc3f97d`.
- Active moderation-command work is #43 / PR #44 / branch `feature/43-moderation-commands`.
- Independent CustomHud/AnoVeto work remains draft PR #40 on `feature/39-custom-hud-anoveto`; its integrated head `b704d661446955599824e2d891009b087d7afa29` passed CI #158 but still requires real CS2 client/server acceptance.
- Extended commands #36 remain a separate workstream.

## Known-good #43 code checkpoint

- Reviewed implementation head before documentation commits: `cec474f06e5f9a45b75e0e366b99ad2074f0ced3`.
- CI run: `35447096351` (#169).
- Release build: passed with zero warnings and zero errors.
- Test suite: 170/170 passed, including MariaDB integration.
- Formatting: passed.
- Development plugin publish: passed.
- Deployment package validation: passed.
- Artifact upload: passed.
- Documentation-only commits follow this checkpoint; require final exact-head CI before merge.

## Implemented in #43 / PR #44

- New standalone `AnoCore.Modules.Admin` project.
- `ModerationTargetGateway` reuses shared online target resolution and target authorization.
- Online targets support exact SteamID64, exact case-insensitive names and unique prefixes; ambiguity is rejected.
- Destructive moderation does not enable multi-target selectors.
- Offline moderation accepts only explicit SteamID64 targets.
- Player-issued offline moderation requires an online actor, the operation permission and central immunity approval.
- Console/server actor remains supported without player permission/immunity checks.
- `ModerationCommandExecutor` maps eight operations to the shared `IModerationService`.
- Duration `0` means permanent; negative and overflowing durations fail before target lookup.
- Reasons are normalized/bounded by shared moderation validation and receive a deterministic default when omitted.
- Missing active restrictions on revoke return a clean invalid-input result.
- Registered commands:
  - `anoban` / `anounban`
  - `anomute` / `anounmute`
  - `anogag` / `anoungag`
  - `anosilence` / `anounsilence`
- Every command has a distinct `ano.admin.*` permission and typed argument metadata.
- Command disposal unregisters all owned registrations.
- Constructor registration is atomic: a later collision rolls back registrations already created by that constructor.
- Detailed behavior is documented in `docs/moderation-commands.md`.

## Scope boundary

PR #44 intentionally remains engine-independent. It does not yet:

- instantiate the admin controller from `AnoCore.Plugin`,
- disconnect clients with active connect restrictions,
- suppress voice for active voice restrictions,
- suppress chat for active chat restrictions,
- implement kick/warnings/admin HUD,
- replace the real-server acceptance gate.

The next #17 native integration package should wire shared services into the plugin and enforce the already-persisted moderation state. It must not duplicate target resolution, permissions, immunity or moderation storage.

## Review status

Reviewed:
- single-target resolution and ambiguity behavior,
- online vs offline SteamID paths,
- console actor behavior,
- permission and immunity reuse,
- self-target denial,
- duration/permanent/overflow behavior,
- reason normalization,
- apply/revoke mapping,
- command metadata,
- permission gate ordering,
- dispose cleanup,
- partial-constructor rollback.

Review finding fixed: command registration now rolls back atomically if a later registration fails.

No remaining engine-independent command blocker is known.

## Next steps

1. Run final CI on the exact #44 documentation head, update PR metadata, self-review and merge with expected-head SHA.
2. Start the next small #17 package from the merged main: native moderation enforcement/composition.
3. Keep native enforcement split into small boundaries: connect-ban first, then chat gag, then voice mute, with a gate after each.
4. Keep PR #40 separate and draft until real CustomHud acceptance is recorded.
5. Keep #36 Extended Commands separate until the shared administration/native-enforcement foundations it needs are merged.

## Integration rules

Use the single shared services from `RuntimeServices`. Do not create another player registry, target resolver, target-authorization layer, permission evaluator or moderation repository. Player commands must authorize before state changes. Native engine calls after asynchronous work must be marshalled onto the server update thread.

License, NOTICE and source provenance must remain intact.
