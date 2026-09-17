# Existing-server data migration

Migration is incremental. Preserve existing server behavior and data while replacing one subsystem at a time with independently verified AnoCore services.

## Principles

1. Preserve attribution and GPL obligations for copied/adapted code.
2. Write characterization/contract tests before replacing behavior where practical.
3. Replace one subsystem at a time.
4. Keep compatibility shims temporary and documented.
5. Do not import opaque DLLs or absolute developer-machine paths into AnoCore.
6. Preserve existing server data through explicit, reversible migrations.

## Planned subsystem order

| Order | Existing capability | AnoCore target |
| --- | --- | --- |
| 1 | Module/API foundation | `AnoCore.Abstractions` + `AnoCore.Runtime` |
| 2 | Player lifecycle/services | Ano player service |
| 3 | Config registration | Ano config service |
| 4 | Command registration | Ano command service |
| 5 | Permissions/admin roles | Ano permission service + Admin module |
| 6 | MySQL player storage | Ano persistence service + migrations |
| 7 | Settings/placeholders/chat | dedicated core services |
| 8 | Stats/ranks/playtime/toplists | independent modules |
| 9 | Maps/votes/veto | `AnoMaps`, `AnoVote`, `AnoVeto` |
| 10 | Match/tournament | `AnoTournament` |

## Data migration

Existing server player data, statistics, playtime, ranks, bans, permissions and configuration will not be modified in-place without a tested migration path. Future migration tooling must support dry-run/validation and preserve a recoverable backup before destructive schema changes.
