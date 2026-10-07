using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class ProgressionAdministrationSchemaMigration019 : IDatabaseMigration
{
    public long Version => 19;
    public string Name => "progression-administration";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_progression_admin_requests (
                request_id CHAR(36) NOT NULL PRIMARY KEY,
                operation TINYINT UNSIGNED NOT NULL,
                target_steam_id BIGINT UNSIGNED NOT NULL,
                amount BIGINT UNSIGNED NOT NULL,
                actor_steam_id BIGINT UNSIGNED NULL,
                reason VARCHAR(384) NOT NULL,
                occurred_at_utc DATETIME(6) NOT NULL,
                previous_xp BIGINT UNSIGNED NOT NULL,
                current_xp BIGINT UNSIGNED NOT NULL,
                revision BIGINT UNSIGNED NOT NULL,
                INDEX ix_ano_progression_admin_target (target_steam_id, revision)
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
