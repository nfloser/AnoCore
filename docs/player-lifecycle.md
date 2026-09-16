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

The registry publishes:

- `PlayerConnectedEvent` for a first active connection;
- `PlayerReconnectedEvent` when the same SteamID replaces an active session;
- `PlayerUpdatedEvent` for accepted state changes;
- `PlayerDisconnectedEvent` after the current session is removed.

Stale updates/disconnects do not publish events.

## CounterStrikeSharp boundary

`AnoCore.Plugin` owns the `CCSPlayerController` mapping. `CounterStrikePlayerMapper` filters invalid controllers, bots, HLTV and zero SteamIDs before creating AnoCore connection/update records.

This keeps `AnoCore.Abstractions` and `AnoCore.Runtime` independent of CounterStrikeSharp and prevents the global static player/controller coupling used by the legacy K4-Zenith player model.

## Persistence

This registry is intentionally in-memory. MySQL/MariaDB loading and saving will be implemented as a separate persistence subsystem so database failures cannot redefine basic connection/session semantics.
