using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class PlaytimeStateSchemaMigration008 : IDatabaseMigration
{
    public long Version => 8;
    public string Name => "playtime-state-segments";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_playtime_segments (
                segment_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
                session_id CHAR(36) NOT NULL,
                steam_id BIGINT UNSIGNED NOT NULL,
                team TINYINT UNSIGNED NOT NULL,
                is_alive BOOLEAN NOT NULL,
                started_at_utc DATETIME(6) NOT NULL,
                accounted_until_utc DATETIME(6) NOT NULL,
                closed_at_utc DATETIME(6) NULL,
                INDEX ix_ano_playtime_segment_player (steam_id, started_at_utc),
                INDEX ix_ano_playtime_segment_session (session_id, closed_at_utc),
                CONSTRAINT fk_ano_playtime_segment_session
                    FOREIGN KEY (session_id) REFERENCES ano_playtime_sessions(session_id)
                    ON DELETE CASCADE
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
