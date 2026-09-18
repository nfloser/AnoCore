# AnoCore development handoff

## Current workstream

- Full functional scope remains in `docs/functional-acceptance.md`; umbrella #11 remains open.
- Centralized targeting/immunity issue #37 / PR #38 is merged in `main` at `ad73aa7cfea8ab4342e599cca8b65146e163551b`.
- Active moderation foundation: issue #41 / PR #42 / branch `feature/41-moderation-state`.
- Independent CustomHud/AnoVeto work remains PR #40 on `feature/39-custom-hud-anoveto`; do not overwrite its HUD/plugin/UI files.
- Extended administration commands #36 remain a separate workstream and must consume the shared targeting/moderation foundations rather than duplicating them.

## Known-good moderation checkpoint

- Fully tested implementation/test head before documentation-only follow-up commits: `339e55446b7f88d19f8aab7b87965f2dd803e543`.
- CI run: `35372648085` (#152).
- Release build: passed with zero warnings and zero errors.
- Test suite: 152/152 passed, including real MariaDB integration.
- Formatting: passed.
- Development plugin publish: passed.
- Deployment package validation: passed.
- Artifact upload: passed.
- Later commits only clean integration-test tables and update moderation/acceptance/handoff documentation; require final CI on the exact final PR head before merge.

## Implemented in issue #41 / PR #42

- CounterStrikeSharp-independent `ModerationRestriction`, sanction, audit, state, repository and service contracts.
- Persistent restriction types: connect, voice and chat; silence is represented atomically as voice + chat sanctions.
- Permanent and expiring sanctions with exact UTC expiry boundaries.
- SteamID-based offline targets and nullable server/console actor identity.
- Immutable sanction objects and validated revocation copies.
- Required, normalized reason text bounded to the MariaDB schema limit.
- Partial revoke semantics: unmute can leave an active gag and vice versa.
- Overlapping restrictions remain active until every active grant is expired or revoked.
- Historical queries remain correct before a later revocation.
- Append-only deterministic sanction/audit history.
- Effective revoke audit records only restrictions that were actually changed.
- Direct repository calls reject inconsistent target/actor/reason/time audit metadata.
- `ModerationSchemaMigration002` creates `ano_moderation_sanctions` and `ano_moderation_audit`.
- Parameterized SQL throughout.
- Sanction creation + audit append share one transaction.
- Revocation locks active rows with `FOR UPDATE`; updates + audit append share one transaction.
- Regression tests prove rollback if audit persistence fails during create or revoke.
- MySqlConnector GUID/string representation differences are handled safely.
- `RuntimeServices` applies migration v2 and exposes exactly one shared `IModerationRepository` and `IModerationService`.
- Runtime restart persistence is integration-tested.
- Detailed behavior and integration boundary are documented in `docs/moderation.md`.

## Scope boundary

PR #42 intentionally does not implement native CS2 enforcement or admin commands. It does not disconnect banned clients, suppress voice/chat, render admin UI or add ban/mute/gag commands.

Subsequent #17 packages must consume:

- `IPlayerTargetResolver`
- `ITargetAuthorizationService`
- `IModerationService`

They must not create a second moderation store, target resolver or immunity implementation.

## Review status

- Reviewed domain invariants, expiry/revocation boundaries, historical queries, SQL parameterization, transaction rollback, row locking, audit accuracy, repository boundary validation and RuntimeServices composition.
- Review findings fixed: historical revocation query semantics, mutable sanction state, schema-length reason validation, effective partial-revoke audit, orphaned revocation metadata and inconsistent direct-repository audit metadata.
- No remaining moderation code blocker is known.
- Native connect/voice/chat enforcement still requires real CS2 acceptance once those adapters are implemented.

## Next steps

1. Run final CI on the exact documentation/handoff head, then update PR #42 metadata, self-review and merge with expected-head SHA if still mergeable.
2. Reconcile/finish independent PR #40 after the moderation merge without overwriting its HUD/AnoVeto work.
3. Continue #17 in small packages: native moderation enforcement adapters, then permissioned admin commands/UI/audit presentation.
4. Keep #36 Extended Commands separate until its shared #17 dependencies are merged.
5. Preserve test-first commits, exact-head CI, self-review and this handoff before context boundaries.

## Integration rules

Use shared services in `RuntimeServices`. Online admin commands must resolve targets through `IPlayerTargetResolver` and authorize through `ITargetAuthorizationService` before calling `IModerationService`. Offline administration must enforce equivalent permissions at its command/API boundary. Engine calls after asynchronous work must be marshalled onto the server update thread.

License, NOTICE and source provenance must remain intact.
