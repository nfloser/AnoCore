using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class WarningSchemaMigration004 : IDatabaseMigration
{
    public long Version => 4;
    public string Name => "admin-warnings";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_admin_warnings (
                warning_id CHAR(36) NOT NULL PRIMARY KEY,
                target_steam_id BIGINT UNSIGNED NOT NULL,
                actor_steam_id BIGINT UNSIGNED NULL,
                reason VARCHAR(512) NOT NULL,
                created_at_utc DATETIME(6) NOT NULL,
                expires_at_utc DATETIME(6) NULL,
                cleared_at_utc DATETIME(6) NULL,
                cleared_by_steam_id BIGINT UNSIGNED NULL,
                clear_reason VARCHAR(512) NULL,
                INDEX ix_ano_warnings_target_history (target_steam_id, created_at_utc, warning_id),
                INDEX ix_ano_warnings_target_active (
                    target_steam_id, cleared_at_utc, expires_at_utc)
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
