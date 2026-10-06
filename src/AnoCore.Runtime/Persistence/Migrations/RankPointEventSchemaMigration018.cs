using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class RankPointEventSchemaMigration018 : IDatabaseMigration
{
    public long Version => 18;
    public string Name => "rank-point-events";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_rank_point_events (
                source_event_id CHAR(36) NOT NULL,
                player_steam_id BIGINT UNSIGNED NOT NULL,
                component VARCHAR(64) NOT NULL,
                points BIGINT NOT NULL,
                occurred_at_utc DATETIME(6) NOT NULL,
                map_name VARCHAR(128) NOT NULL,
                PRIMARY KEY (source_event_id, player_steam_id, component),
                INDEX ix_ano_rank_point_events_player (
                    player_steam_id, occurred_at_utc)
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
