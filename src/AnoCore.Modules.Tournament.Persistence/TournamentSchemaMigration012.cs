using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Tournament.Persistence;

public sealed class TournamentSchemaMigration012 : IDatabaseMigration
{
    public long Version => 12;
    public string Name => "tournament-match-recovery";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        foreach (var sql in Statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static readonly string[] Statements =
    [
        """
        CREATE TABLE IF NOT EXISTS ano_tournament_matches (
            match_id CHAR(36) NOT NULL PRIMARY KEY,
            best_of TINYINT UNSIGNED NOT NULL,
            knife_round BOOLEAN NOT NULL,
            overtime_enabled BOOLEAN NOT NULL,
            team_a_name VARCHAR(64) NOT NULL,
            team_a_tag VARCHAR(12) NOT NULL,
            team_a_captain BIGINT UNSIGNED NOT NULL,
            team_b_name VARCHAR(64) NOT NULL,
            team_b_tag VARCHAR(12) NOT NULL,
            team_b_captain BIGINT UNSIGNED NOT NULL,
            snapshot_json LONGTEXT NOT NULL,
            revision BIGINT UNSIGNED NOT NULL,
            updated_at_utc DATETIME(6) NOT NULL
        ) ENGINE=InnoDB
        """,
        """
        CREATE TABLE IF NOT EXISTS ano_tournament_members (
            match_id CHAR(36) NOT NULL,
            team_slot TINYINT UNSIGNED NOT NULL,
            steam_id BIGINT UNSIGNED NOT NULL,
            PRIMARY KEY (match_id, steam_id),
            INDEX ix_ano_tournament_members_team (match_id, team_slot),
            CONSTRAINT fk_ano_tournament_members_match
                FOREIGN KEY (match_id) REFERENCES ano_tournament_matches(match_id)
                ON DELETE CASCADE
        ) ENGINE=InnoDB
        """,
        """
        CREATE TABLE IF NOT EXISTS ano_tournament_runtime (
            singleton_id TINYINT UNSIGNED NOT NULL PRIMARY KEY,
            active_match_id CHAR(36) NULL,
            CONSTRAINT fk_ano_tournament_runtime_active
                FOREIGN KEY (active_match_id) REFERENCES ano_tournament_matches(match_id)
                ON DELETE SET NULL
        ) ENGINE=InnoDB
        """,
        """
        INSERT INTO ano_tournament_runtime (singleton_id, active_match_id)
        VALUES (1, NULL)
        ON DUPLICATE KEY UPDATE singleton_id = singleton_id
        """,
    ];
}
