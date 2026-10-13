using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Tournament.Persistence;

public sealed class TournamentPlayoffSchemaMigration023 : IDatabaseMigration
{
    public long Version => 23;
    public string Name => "tournament-playoff-results";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            ALTER TABLE ano_tournament_events
            ADD COLUMN IF NOT EXISTS playoff_results_json LONGTEXT NOT NULL DEFAULT '[]'
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
