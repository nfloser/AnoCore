using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class ModerationSchemaMigration002 : IDatabaseMigration
{
    public long Version => 2;

    public string Name => "moderation-sanctions-and-audit";

    public async ValueTask ApplyAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS ano_moderation_sanctions (
                sanction_id CHAR(36) NOT NULL PRIMARY KEY,
                target_steam_id BIGINT UNSIGNED NOT NULL,
                actor_steam_id BIGINT UNSIGNED NULL,
                restriction TINYINT UNSIGNED NOT NULL,
                reason VARCHAR(512) NOT NULL,
                created_at_utc DATETIME(6) NOT NULL,
                expires_at_utc DATETIME(6) NULL,
                revoked_at_utc DATETIME(6) NULL,
                revoked_by_steam_id BIGINT UNSIGNED NULL,
                revocation_reason VARCHAR(512) NULL,
                INDEX ix_ano_moderation_sanctions_target_created (target_steam_id, created_at_utc),
                INDEX ix_ano_moderation_sanctions_target_active (
                    target_steam_id,
                    revoked_at_utc,
                    expires_at_utc)
            ) ENGINE=InnoDB
            """, cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS ano_moderation_audit (
                audit_id CHAR(36) NOT NULL PRIMARY KEY,
                target_steam_id BIGINT UNSIGNED NOT NULL,
                actor_steam_id BIGINT UNSIGNED NULL,
                action TINYINT UNSIGNED NOT NULL,
                restrictions TINYINT UNSIGNED NOT NULL,
                reason VARCHAR(512) NOT NULL,
                occurred_at_utc DATETIME(6) NOT NULL,
                INDEX ix_ano_moderation_audit_target_time (target_steam_id, occurred_at_utc)
            ) ENGINE=InnoDB
            """, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ExecuteAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
