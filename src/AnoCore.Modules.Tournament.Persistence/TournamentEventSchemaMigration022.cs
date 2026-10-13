using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Tournament.Persistence;

/// <summary>
/// Separate event-wide state from legacy singleton live-match ownership.
/// Never change ano_tournament_runtime in an event migration.
/// </summary>
public sealed class TournamentEventSchemaMigration022 : IDatabaseMigration
{
    public long Version => 22;
    public string Name => "tournament-shared-events";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        foreach (var statement in Statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static readonly string[] Statements =
    [
        """
        CREATE TABLE IF NOT EXISTS ano_tournament_events (
            event_id VARCHAR(48) NOT NULL PRIMARY KEY,
            definition_json LONGTEXT NOT NULL,
            results_json LONGTEXT NOT NULL,
            revision BIGINT UNSIGNED NOT NULL,
            updated_at_utc DATETIME(6) NOT NULL
        ) ENGINE=InnoDB
        """,
        """
        CREATE TABLE IF NOT EXISTS ano_tournament_event_runtime (
            singleton_id TINYINT UNSIGNED NOT NULL PRIMARY KEY,
            active_event_id VARCHAR(48) NULL,
            CONSTRAINT fk_ano_tournament_event_runtime_event
                FOREIGN KEY (active_event_id) REFERENCES ano_tournament_events(event_id)
                ON DELETE SET NULL
        ) ENGINE=InnoDB
        """,
        """
        INSERT INTO ano_tournament_event_runtime (singleton_id, active_event_id)
        VALUES (1, NULL)
        ON DUPLICATE KEY UPDATE singleton_id = singleton_id
        """,
    ];
}
