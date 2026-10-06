using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class ProgressionChallengeSchemaMigration017 : IDatabaseMigration
{
    public long Version => 17;
    public string Name => "progression-challenge-completions";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_progression_challenges (
                player_steam_id BIGINT UNSIGNED NOT NULL,
                challenge_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                starts_at_utc DATETIME(6) NOT NULL,
                ends_at_utc DATETIME(6) NOT NULL,
                definition_version INT UNSIGNED NOT NULL,
                grant_id VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                PRIMARY KEY (player_steam_id, challenge_id, starts_at_utc),
                UNIQUE KEY ux_ano_challenge_grant (player_steam_id, grant_id),
                CONSTRAINT fk_ano_challenge_grant
                    FOREIGN KEY (player_steam_id, grant_id)
                    REFERENCES ano_progression_grants(player_steam_id, grant_id)
                    ON DELETE CASCADE
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
