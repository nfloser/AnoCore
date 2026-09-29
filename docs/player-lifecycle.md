# Player lifecycle

AnoCore treats the active player list as runtime state rather than a global static collection tied directly to CounterStrikeSharp objects.

## Goals

- expose stable player data to modules without leaking `CCSPlayerController` into the core API;
- guarantee at most one active session per SteamID64;
- make reconnect behavior explicit;
- protect a new connection from delayed events belonging to an older connection;
- keep persistence, permissions, ranks and statistics outside this lifecycle layer.

## Identity

`PlayerId` identifies the Steam account. `PlayerSessionId` identifies one concrete connection of that Steam account to the server.

```text
SteamID 7656...
   │
   ├── Session A ── disconnect/reconnect
   │
   └── Session B ── current active session
```

Every reconnect receives a new random session ID. Updates and disconnects must contain the current session ID. An event carrying Session A after Session B has replaced it is ignored rather than removing or mutating Session B.

## State

`PlayerSnapshot` is immutable and contains:

- Steam identity and session identity;
- display name;
- connection/alive state;
- normalized Ano team (`Unknown`, `Spectator`, `Terrorist`, `CounterTerrorist`);
- connection and last-update timestamps.

The online registry only contains connected snapshots. A disconnected snapshot is emitted with `IsConnected = false` and then removed from the registry.

## Lifecycle events

The in-memory registry publishes:

- `PlayerConnectedEvent` for a first active connection;
- `PlayerReconnectedEvent` when the same SteamID replaces an active session;
- `PlayerUpdatedEvent` for accepted state changes;
- `PlayerDisconnectedEvent` after the current session is removed.

Stale updates/disconnects do not publish events.

After runtime persistence is available, modules can also subscribe to:

- `PlayerProfileLoadedEvent`, emitted only after the current session profile upsert commits;
- `PlayerProfileUnloadedEvent`, emitted only after the leaving session profile upsert commits.

Both events carry the immutable session snapshot and its durable profile. Runtime startup applies the same loaded path to humans who connected before database initialization. Reconnect emits an unload for the replaced session and a load for the new session. A delayed older load is discarded when its session is no longer current. Persistence failure or cancellation emits no durable lifecycle event; subscriber failures after a commit are isolated.

## CounterStrikeSharp boundary

`AnoCore.Plugin` owns the `CCSPlayerController` mapping. `CounterStrikePlayerMapper` filters invalid controllers, bots, HLTV and zero SteamIDs before creating AnoCore connection/update records.

This keeps `AnoCore.Abstractions` and `AnoCore.Runtime` independent of CounterStrikeSharp and avoids global static coupling between player state and engine controllers.

## Persistence

This registry is intentionally in-memory. MySQL/MariaDB profile persistence is a separate runtime subsystem, so database failures do not redefine the registry's connection/session state. Durable profile lifecycle events deliberately report persistence readiness rather than raw engine connectivity.
