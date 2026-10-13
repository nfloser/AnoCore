using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Tournament.Persistence;

public sealed class TournamentPresenceSchemaMigration024 : IDatabaseMigration
{
    public long Version => 24;
    public string Name => "tournament-server-presence";
    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_tournament_server_presence (
                server_id VARCHAR(48) NOT NULL PRIMARY KEY,
                instance_id CHAR(36) NOT NULL,
                players_json LONGTEXT NOT NULL,
                expires_at_utc DATETIME(6) NOT NULL,
                INDEX ix_ano_tournament_presence_expiry (expires_at_utc)
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
