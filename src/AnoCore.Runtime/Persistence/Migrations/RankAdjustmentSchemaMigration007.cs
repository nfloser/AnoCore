using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class RankAdjustmentSchemaMigration007 : IDatabaseMigration
{
    public long Version => 7;
    public string Name => "rank-adjustments";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_rank_adjustments (
                player_steam_id BIGINT UNSIGNED NOT NULL PRIMARY KEY,
                points BIGINT NOT NULL,
                updated_by_steam_id BIGINT UNSIGNED NULL,
                updated_at_utc DATETIME(6) NOT NULL
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
