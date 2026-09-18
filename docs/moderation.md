# Moderation runtime

AnoCore moderation is a CounterStrikeSharp-independent runtime service for durable bans and communication restrictions. Native game enforcement and player-facing administration commands are intentionally separate consumers of this service.

## Restrictions

Each persisted sanction contains exactly one restriction:

- `Connect`: prevents a player from joining or remaining connected once native enforcement is wired.
- `Voice`: prevents voice communication once the voice adapter is wired.
- `Chat`: prevents text chat once the chat processor is wired.

A silence operation is represented as two sanctions, `Voice | Chat`, created atomically with one audit entry. This keeps later partial reversal deterministic: an unmute can revoke only `Voice` while `Chat` remains active.

## Identity and actors

Targets are stored by SteamID64 and do not need to be online or already present in the player profile table. The actor may be a SteamID64 or `null` for server/console actions.

User-facing admin commands must resolve and authorize online targets through the shared `IPlayerTargetResolver` and `ITargetAuthorizationService` before mutating moderation state. Offline administration must perform equivalent permission checks at its command/API boundary.

## Time semantics

All timestamps are normalized to UTC.

- Permanent sanctions have no expiry.
- Temporary sanctions are active while `instant < ExpiresAtUtc`.
- A sanction is inactive starting exactly at its expiry.
- A revoked sanction is active only for historical queries where `instant < RevokedAtUtc`.
- Revocation changes history; it never deletes the original sanction.

Historical state queries therefore remain correct even after a later revoke.

## Persistence

Schema migration `ModerationSchemaMigration002` creates:

- `ano_moderation_sanctions`
- `ano_moderation_audit`

`MySqlModerationRepository` uses parameterized SQL throughout. Applying sanctions writes the sanction rows and audit row in one transaction. Revocation locks matching active rows with `FOR UPDATE`, updates them and appends the audit row in the same transaction. If the audit insert fails, the sanction insert/update is rolled back.

Audit rows are append-only. For partial revoke operations, the persisted audit restrictions describe only the restrictions that were actually changed.

## Runtime services

`RuntimeServices` exposes one shared:

- `IModerationRepository`
- `IModerationService`

Modules should resolve these shared services rather than construct another repository or moderation state manager.

The service accepts offline targets and console actors, validates known restriction flags, requires non-empty reasons, and bounds reasons to the persistence schema limit.

## Native enforcement boundary

This foundation does not itself:

- disconnect banned clients,
- suppress voice packets,
- suppress chat messages,
- provide `!anoban`, `!anomute`, `!anogag` or related commands,
- render an admin menu.

Those are subsequent #17 work packages. They must reuse the shared moderation service, centralized target authorization and audit history rather than duplicating state.

## Acceptance evidence

CI covers permanent and temporary sanctions, exact expiry boundaries, combined silence and partial revoke, overlapping sanctions, offline targets, console actors, restart persistence, historical state before a later revoke, effective audit restrictions, inconsistent audit rejection, and transaction rollback when audit persistence fails.

Native CS2 enforcement still requires real-server acceptance once the adapters and commands are implemented.
