using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class AdminAuditSchemaMigration003 : IDatabaseMigration
{
    public long Version => 3;

    public string Name => "admin-action-audit";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_admin_action_audit (
                audit_id CHAR(36) NOT NULL PRIMARY KEY,
                action_id VARCHAR(64) NOT NULL,
                actor_steam_id BIGINT UNSIGNED NULL,
                target_steam_id BIGINT UNSIGNED NULL,
                reason VARCHAR(512) NOT NULL,
                occurred_at_utc DATETIME(6) NOT NULL,
                INDEX ix_ano_admin_audit_time (occurred_at_utc, audit_id),
                INDEX ix_ano_admin_audit_target_time (target_steam_id, occurred_at_utc, audit_id)
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
