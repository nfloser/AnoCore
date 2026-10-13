using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Tournament.Persistence;

public sealed class TournamentServerMatchSchemaMigration025 : IDatabaseMigration
{
    public long Version => 25;
    public string Name => "tournament-server-active-matches";
    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_tournament_server_runtime (
                server_id VARCHAR(48) NOT NULL PRIMARY KEY,
                active_match_id CHAR(36) NULL,
                UNIQUE KEY uq_ano_tournament_scoped_active (active_match_id),
                CONSTRAINT fk_ano_tournament_scoped_active FOREIGN KEY (active_match_id)
                    REFERENCES ano_tournament_matches(match_id) ON DELETE SET NULL
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
