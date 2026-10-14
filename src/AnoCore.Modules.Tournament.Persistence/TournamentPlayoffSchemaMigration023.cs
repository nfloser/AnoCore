using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Tournament.Persistence;

public sealed class TournamentPlayoffSchemaMigration023 : IDatabaseMigration
{
    public long Version => 23;
    public string Name => "tournament-playoff-results";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        // MySQL does not universally support ADD COLUMN IF NOT EXISTS. Read the
        // actual schema so a partially applied migration is safe to retry.
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT IS_NULLABLE
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'ano_tournament_events'
              AND COLUMN_NAME = 'playoff_results_json'
            """;
        var nullable = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (nullable is null)
        {
            // Existing events must be backfilled before enforcing NOT NULL.
            command.CommandText = """
                ALTER TABLE ano_tournament_events
                ADD COLUMN playoff_results_json LONGTEXT NULL
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            nullable = "YES";
        }

        command.CommandText = """
            UPDATE ano_tournament_events
            SET playoff_results_json = '[]'
            WHERE playoff_results_json IS NULL OR playoff_results_json = ''
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (string.Equals(nullable, "YES", StringComparison.OrdinalIgnoreCase))
        {
            command.CommandText = """
                ALTER TABLE ano_tournament_events
                MODIFY COLUMN playoff_results_json LONGTEXT NOT NULL
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
