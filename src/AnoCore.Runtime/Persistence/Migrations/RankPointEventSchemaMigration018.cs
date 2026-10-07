using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class RankPointEventSchemaMigration018 : IDatabaseMigration
{
    public long Version => 18;
    public string Name => "rank-point-events";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        foreach (var sql in new[]
        {
            """
            CREATE TABLE IF NOT EXISTS ano_rank_point_batches (
                event_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
                source VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                occurred_at_utc DATETIME(6) NOT NULL,
                payload_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL
            ) ENGINE=InnoDB
            """,
            """
            CREATE TABLE IF NOT EXISTS ano_rank_point_events (
                event_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                player_steam_id BIGINT UNSIGNED NOT NULL,
                points BIGINT NOT NULL,
                PRIMARY KEY (event_id, player_steam_id),
                KEY ix_rank_events_player (player_steam_id)
            ) ENGINE=InnoDB
            """,
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
