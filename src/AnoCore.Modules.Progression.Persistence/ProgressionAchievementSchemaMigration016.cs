using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class ProgressionAchievementSchemaMigration016 : IDatabaseMigration
{
    public long Version => 16;
    public string Name => "progression-permanent-achievements";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_progression_achievements (
                player_steam_id BIGINT UNSIGNED NOT NULL,
                achievement_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                tier SMALLINT UNSIGNED NOT NULL,
                definition_version INT UNSIGNED NOT NULL,
                grant_id VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                PRIMARY KEY (player_steam_id, achievement_id, tier),
                UNIQUE KEY ux_ano_achievement_grant (player_steam_id, grant_id),
                CONSTRAINT fk_ano_achievement_grant
                    FOREIGN KEY (player_steam_id, grant_id)
                    REFERENCES ano_progression_grants(player_steam_id, grant_id)
                    ON DELETE CASCADE
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
