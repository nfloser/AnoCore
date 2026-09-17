# AnoCore

AnoCore is a modular Counter-Strike 2 server framework for the AnoMeme server ecosystem. Shared services support independent administration, statistics, map voting and tournament modules.

> **Status:** private development. The plugin initializes shared services after database validation and reports readiness through `!anostatus`. Official gameplay modules are tracked in the [functional acceptance matrix](docs/functional-acceptance.md). No production release exists.

## Architecture

- `AnoCore.Abstractions`: module-facing contracts without engine dependencies.
- `AnoCore.Runtime`: lifecycle, events, configuration, localization, placeholders, persistence, authorization, commands, menus, settings, map catalog and voting implementations.
- `AnoCore.Plugin`: CounterStrikeSharp adapters and the server composition root.
- Gameplay features remain independent modules.

See [architecture](docs/architecture.md), [player lifecycle](docs/player-lifecycle.md) and [data migration](docs/data-migration.md).

## Install and verify

[Deployment instructions](docs/deployment.md) explain the CI development package, prerequisites, rollback and server checks.

Use `!anostatus` in chat or `css_anostatus` in the server console to confirm that the plugin responds and reports tracked humans. The command reports `starting`, `not configured`, `startup failed` or `ready` and the number of optional modules. `ready` means shared services initialized successfully, not that every gameplay feature is complete.

## Build

Requires .NET 10 SDK and a server with CounterStrikeSharp API 374 or newer and a compatible .NET 10 host. The API package is pinned to `CounterStrikeSharp.API` 1.0.374.

```bash
dotnet restore AnoCore.sln
dotnet build AnoCore.sln --configuration Release --no-restore
dotnet test AnoCore.sln --configuration Release --no-build
dotnet format AnoCore.sln --verify-no-changes --no-restore
dotnet publish src/AnoCore.Plugin/AnoCore.Plugin.csproj --configuration Release --no-build --output artifacts/plugins/AnoCore
```

CI runs database integration tests with MariaDB and validates the deployment package before uploading it. Native CS2 verification remains a separate [acceptance gate](docs/runtime-verification.md).

## Development workflow

Issue → Branch → Tests/TDD → Implementation → Documentation → Pull Request → CI → Review → Corrections → Merge → Release.

See [AGENTS.md](AGENTS.md), [CONTRIBUTING.md](CONTRIBUTING.md) and [current handoff](docs/HANDOFF.md).

## License

GNU GPL v3.0. Third-party attribution and applicable notices are preserved in [NOTICE.md](NOTICE.md) and [LICENSE.md](LICENSE.md).
