# Persistence and migrations

AnoCore uses `MySqlConnector` behind provider-neutral `System.Data.Common` contracts. Gameplay modules consume `IDatabase`, `IPlayerRepository` or `IModuleDataStore` instead of depending on MySQL-specific classes.

## Connection model

`MySqlDatabase` opens a connection per operation. MySqlConnector pooling remains enabled, so logical connection lifetime is short while physical connections are reused by the provider.

- `WithConnectionAsync` owns and disposes one open connection.
- `InTransactionAsync` commits only after the callback succeeds.
- Callback failures trigger rollback; a rollback failure is surfaced together with the original exception.
- `PingAsync` provides an explicit health signal without exposing credentials.

Connection strings are configuration/secrets only. They must never be committed to repository configuration. The credentials in CI are disposable test-only values.

## Migrations

`MigrationRunner` creates `ano_schema_migrations`, validates migration versions, applies migrations in ascending order and records a version only after its migration callback succeeds.

Migration requirements:

- versions are positive and unique;
- names are non-empty;
- migrations should be safe to retry when practical, especially DDL because MariaDB can implicitly commit schema changes;
- user-controlled data must always use parameters.

`CoreSchemaMigration001` creates the player profile and module-data tables.

`AdminAuditSchemaMigration003` adds an indexed generic administrative action audit table. Its records are separate from moderation sanction history. Action IDs and reasons are validated before insertion; actor and target are optional for console and server-wide actions. The repository uses parameters for all values, returns the newest bounded records and preserves them across service restarts. `IAdminAuditService` and `IAdminAuditRepository` are available through the shared runtime. Administrative commands must record through the service after a successful action; this migration does not itself add command handlers.

## Player persistence

`MySqlPlayerRepository` stores one row per SteamID64. Upsert semantics preserve the earliest `first_seen_utc`, retain the latest `last_seen_utc`, and update the last known player name.

## Module data

`MySqlModuleDataStore` stores JSON strings under `(module_id, data_key)`. Module ID is part of the primary key, preventing two modules using the same logical key from overwriting each other. Keys are restricted to a safe identifier subset.

## Startup behavior

`DatabaseStartupProbe.EnsureReadyAsync` first verifies connectivity and then applies pending migrations. A server deployment may choose to fail AnoCore startup or disable persistence-dependent modules if this probe fails; it must not silently continue while pretending persistence is healthy.

## CI

GitHub Actions runs the persistence integration tests against a pinned MariaDB 11.8.9 service. Tests cover health checks, successful transactions, rollback, migration idempotency, player upsert/read behavior and module namespace isolation.

## Backup and upgrade

Before a production upgrade that introduces migrations:

1. stop writes or enter maintenance mode;
2. take a database backup/snapshot;
3. deploy AnoCore and allow migrations to complete;
4. verify `ano_schema_migrations` and server health;
5. retain the backup until the new deployment has passed runtime acceptance checks.
