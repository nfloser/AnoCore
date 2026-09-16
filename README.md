# AnoCore

AnoCore is a modular Counter-Strike 2 server framework for the AnoMeme server ecosystem. It is designed to provide stable shared infrastructure for independent modules such as administration, statistics, maps, voting, veto and tournament management.

The project uses the proven ideas and implementation experience of K4-Zenith as a migration/reference foundation instead of rediscovering every CounterStrikeSharp integration from scratch. AnoCore is **not** intended to remain a cosmetic rename: subsystems are being characterized, tested and migrated into an Ano-native architecture incrementally.

> **Status:** private early development. No stable public API or production release exists yet.

## Architecture

```text
Counter-Strike 2
      │
CounterStrikeSharp
      │
AnoCore.Plugin
      │
AnoCore.Runtime
      │
AnoCore.Abstractions
      │
┌─────┼────────┬────────┬─────────────┐
Admin Stats   Maps     Veto      Tournament
```

- `AnoCore.Abstractions` contains module-facing contracts and has no CounterStrikeSharp dependency.
- `AnoCore.Runtime` implements core lifecycle/services.
- `AnoCore.Plugin` is the thin CounterStrikeSharp adapter and composition root.
- Optional gameplay/community features are separate modules rather than hard-coded core behavior.

See [`docs/architecture.md`](docs/architecture.md) for the architecture rules, [`docs/player-lifecycle.md`](docs/player-lifecycle.md) for player session semantics and [`docs/migration-from-k4-zenith.md`](docs/migration-from-k4-zenith.md) for the migration strategy.

## Current foundation

AnoCore currently contains the tested module lifecycle and a reconnect-safe in-memory player registry. Player state is independent of CounterStrikeSharp; the plugin layer maps valid human `CCSPlayerController` instances into immutable AnoCore snapshots. Persistence, configs, permissions and higher-level feature modules follow in separate issues/PRs.

## Build

Requires .NET 10 SDK. This follows the current stable CounterStrikeSharp API package, which targets `net10.0`.

```bash
dotnet restore AnoCore.sln
dotnet build AnoCore.sln --configuration Release --no-restore
dotnet test AnoCore.sln --configuration Release --no-build
dotnet format AnoCore.sln --verify-no-changes --no-restore
```

CounterStrikeSharp is pinned through central package management rather than a wildcard package version. The initial foundation targets stable `CounterStrikeSharp.API` 1.0.374.

## Development workflow

AnoCore uses:

**Issue → Branch → Tests/TDD → Implementation → Documentation → Pull Request → CI → Review → Corrections → Merge → Release**

Project-specific development rules are documented in [`AGENTS.md`](AGENTS.md) and contribution instructions in [`CONTRIBUTING.md`](CONTRIBUTING.md).

## Licensing and K4-Zenith attribution

AnoCore is developed under GNU GPL v3.0. K4-Zenith-derived code retains its original copyright and GPL obligations. See [`LICENSE.md`](LICENSE.md) and [`NOTICE.md`](NOTICE.md).
