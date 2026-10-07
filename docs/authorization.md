# Authorization

AnoCore authorization is independent from CounterStrikeSharp and gameplay modules. Commands and modules depend on `IAuthorizationService`; persistence is provided through `IAuthorizationStore` backed by the core module-data store.

## Model

- `RoleId` identifies a role/group.
- `AuthorizationRole` contains parent roles, permission rules, an immunity value and generic tags.
- `PlayerAuthorization` assigns roles and optional direct rules to one SteamID64.
- `AuthorizationState` is a validated immutable policy snapshot.
- `PermissionRule` accepts exact `ano.*`-namespace permissions or a trailing hierarchical wildcard such as `ano.admin.*`.

VIP is intentionally not a separate gameplay type. A role may carry a generic `vip` tag (or any other validated tag), and modules can query `HasTagAsync`. This keeps the core reusable for supporter, donor, staff or tournament-specific group concepts.

## Permission precedence

Authorization is deterministic and explainable:

1. direct player rules are evaluated before role rules;
2. within the selected scope, the most specific matching rule wins;
3. when equally specific rules conflict, `Deny` wins;
4. otherwise access is denied by default.

Examples:

- direct `allow ano.admin.kick` overrides a role-level deny for the same permission;
- role `deny ano.admin.ban` beats role `allow ano.admin.*` because the exact rule is more specific;
- equal exact allow/deny role rules resolve to deny.

`AuthorizationDecision` exposes the effect, source, matching rule and role (when applicable), so callers can explain why a decision was made.

## Inheritance and validation

Role inheritance is transitive. Construction of an `AuthorizationState` rejects:

- duplicate role IDs;
- references to unknown parent roles;
- cyclic inheritance;
- duplicate player assignments;
- assignments to unknown roles.

A state is validated before it can become active.

## Immunity

A player's effective immunity is the maximum immunity value across all assigned and inherited roles. For administrative targeting:

- a player may target themself;
- otherwise the actor must have strictly greater immunity than the target;
- equal or lower immunity cannot target the target player.

This comparison is separate from permission checks: an admin action normally requires both the command permission and an allowed immunity comparison.

## Reload behavior

`AuthorizationService` compiles an immutable lookup snapshot. `ReloadAsync` loads and validates a complete replacement first, then swaps the active snapshot atomically. In-flight readers therefore see either the old complete policy or the new complete policy, never a partially updated graph.

## Persistence

`ModuleDataAuthorizationStore` serializes the validated state through `IModuleDataStore` under the `authorization` module namespace and `state` key. The underlying MySQL/MariaDB store from the persistence workstream provides durable storage without coupling authorization to a database provider.

Future admin tooling should mutate authorization by constructing/validating a replacement state, persisting it, then requesting a reload; it should not mutate internal service dictionaries directly.

## Role administration and timed VIP grants

The shared `authorization/state` record now supports optional `TimedRoles` per
player. Existing permanent `Roles`, rules, JSON and the original SDK constructor
remain compatible. A timed role contributes its complete inherited permissions,
tags and immunity only while UTC is strictly before its expiry. Reads enforce the
boundary without a reload; the native one-second refresh invalidates warmed tag
snapshots. Expired records remain available for inspection; they are not renewed
by reconnect or restart.

Supported commands:

- `anoroledefine <role> <immunity> <permissions> [tags] [parents]`: server console
  only, comma-separated entries, `-` for none, `deny:ano.permission` for denies.
  Complete graph validation rejects unknown parents and cycles before commit.
- `anograntrole <SteamID64> <role> <minutes> [reason]`: 0 permanent; otherwise
  1–5,256,000 minutes. Use explicit IDs for both online and offline players.
- `anorevokerole <SteamID64> <role> <minutes> [reason]`: removes permanent/timed
  assignment. Supply 0 for the unused duration argument.
- `anoroles [SteamID64]`: inspect definitions or bounded assignments.

Grant/revoke/inspect permissions are `ano.admin.roles.grant`, `.revoke`, `.inspect`.
The console can bootstrap definitions and assignments. Players cannot alter their
own grants, target equal/higher immunity, or delegate a role they do not permanently
hold (including inherited parents). The delegated role's inherited immunity must
remain below the actor's. Actors with explicit denial rules cannot delegate roles;
use console administration for those restricted policies. Timed authority cannot
create permanent authority. Definitions are intentionally console-only.

Policy mutation and audit use one MariaDB transaction under the existing policy
row lock; concurrent grants cannot overwrite each other. Audit failure rolls back
the policy. After commit the host reloads authorization without caller cancellation.
If that read fails, the host fails closed until `anoreloadauth` succeeds. No new
schema or competing permission store is introduced. Existing state writers must
not overwrite policies from stale snapshots.

Native acceptance: bootstrap a subordinate VIP role, grant a short duration to an
offline ID, connect and verify permission/tag/immunity, wait for expiry, reconnect,
restart and verify no renewed grant. Check audit, denied delegation and revoke.
