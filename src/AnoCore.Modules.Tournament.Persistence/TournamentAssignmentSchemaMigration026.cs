using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Tournament.Persistence;

public sealed class TournamentAssignmentSchemaMigration026 : IDatabaseMigration
{
    public long Version => 26;
    public string Name => "tournament-matchzy-assignments";
    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_tournament_assignments (
                assignment_id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                event_id VARCHAR(48) NOT NULL,
                fixture_id VARCHAR(192) NOT NULL,
                server_id VARCHAR(48) NOT NULL,
                instance_id CHAR(36) NOT NULL,
                home_team_id VARCHAR(48) NOT NULL,
                away_team_id VARCHAR(48) NOT NULL,
                is_playoff BOOLEAN NOT NULL,
                best_of INT NOT NULL,
                maps_json TEXT NOT NULL,
                definition_json LONGTEXT NOT NULL,
                token_hash CHAR(64) NOT NULL,
                state VARCHAR(16) NOT NULL,
                results_json LONGTEXT NOT NULL,
                updated_at_utc DATETIME(6) NOT NULL,
                KEY ix_ano_assignment_event (event_id, state),
                KEY ix_ano_assignment_server (server_id, state),
                CONSTRAINT fk_ano_assignment_event FOREIGN KEY (event_id)
                    REFERENCES ano_tournament_events(event_id)
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_tournament_assignment_audit (
                audit_id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                assignment_id BIGINT NOT NULL,
                action VARCHAR(32) NOT NULL,
                detail VARCHAR(128) NOT NULL,
                occurred_at_utc DATETIME(6) NOT NULL,
                CONSTRAINT fk_ano_assignment_audit FOREIGN KEY (assignment_id)
                    REFERENCES ano_tournament_assignments(assignment_id)
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
