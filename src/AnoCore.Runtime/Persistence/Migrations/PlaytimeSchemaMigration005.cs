using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class PlaytimeSchemaMigration005 : IDatabaseMigration
{
    public long Version => 5;
    public string Name => "player-playtime-sessions";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_playtime_sessions (
                session_id CHAR(36) NOT NULL PRIMARY KEY,
                steam_id BIGINT UNSIGNED NOT NULL,
                started_at_utc DATETIME(6) NOT NULL,
                accounted_until_utc DATETIME(6) NOT NULL,
                closed_at_utc DATETIME(6) NULL,
                INDEX ix_ano_playtime_player (steam_id, started_at_utc)
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
