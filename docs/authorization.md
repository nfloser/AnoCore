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
