using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class GameplayStatSchemaMigration010 : IDatabaseMigration
{
    public long Version => 10;
    public string Name => "gameplay-stat-events";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_gameplay_stats (
                event_id CHAR(36) NOT NULL PRIMARY KEY,
                player_steam_id BIGINT UNSIGNED NOT NULL,
                occurred_at_utc DATETIME(6) NOT NULL,
                map_name VARCHAR(128) NOT NULL,
                stat_kind TINYINT UNSIGNED NOT NULL,
                amount SMALLINT UNSIGNED NOT NULL,
                INDEX ix_ano_gameplay_stats_player (
                    player_steam_id, map_name, stat_kind, occurred_at_utc)
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
