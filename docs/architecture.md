# AnoCore architecture

## Goal

AnoCore provides reusable infrastructure for CS2 server modules without forcing each feature to reimplement player lifecycle, commands, permissions, configuration, persistence, events or UI integration.

AnoCore separates engine adapters from service implementations, avoids hard-coded local dependencies and evolves public contracts deliberately.

## Dependency direction

```text
Counter-Strike 2
      │
CounterStrikeSharp
      │
AnoCore.Plugin          ← thin CS2 adapter / composition root
      │
AnoCore.Runtime         ← implementation of core services
      │
AnoCore.Abstractions    ← stable module-facing contracts
      │
Optional Ano modules
```

Dependencies must point toward abstractions. `AnoCore.Abstractions` must not reference CounterStrikeSharp, MySQL, YAML libraries or optional modules. This keeps contracts unit-testable and makes infrastructure replaceable.

## Initial contracts

The foundation currently establishes:

- normalized `ModuleId` identifiers;
- `IAnoModule` lifecycle contracts;
- `ModuleHost` state transitions, duplicate protection and initialization rollback;
- validated `PlayerId` and `PlayerSessionId` identities;
- immutable `PlayerSnapshot` state;
- reconnect-safe `IPlayerRegistry` lifecycle contracts and events;
- `PermissionId` using the `ano.*` namespace;
- command descriptors and registry contracts;
- event-bus contracts;
- a thin CounterStrikeSharp plugin/adapter boundary.

Identifiers are immutable reference value objects rather than structs. This prevents callers from bypassing constructor validation through `default(T)` and creating invalid IDs.

Player lifecycle state is identified by both SteamID and a per-connection session ID. This allows the runtime to reject stale updates/disconnects after a reconnect. See [`player-lifecycle.md`](player-lifecycle.md).

## Module boundaries

The following are intentionally **not** part of the core runtime:

- administration and punishments;
- ranks, statistics and playtime;
- custom tags and toplists;
- maps, voting and veto;
- tournament/match management;
- web dashboard features.

They will consume stable AnoCore services as independent modules.

## Module lifecycle

A module moves through these states:

```text
Created → Loading → Loaded → Unloading → Unloaded
              └──────────────→ Faulted ←──────────┘
```

If initialization fails, the runtime makes a best-effort call to `ShutdownAsync` before recording the module as faulted. Module shutdown logic must therefore tolerate partial initialization. If rollback also fails, both failures are retained in the module snapshot.

A duplicate active module ID is rejected.

## Compatibility policy

Until the first stable release, APIs may evolve between development versions. Once `1.0.0` is reached, breaking public API changes require a major semantic-version increment or an explicit deprecation/migration path.
