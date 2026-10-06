using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class ProgressionAchievementSchemaMigration016 : IDatabaseMigration
{
    public long Version => 16;

    public string Name => "progression-achievement-unlocks";

    public async ValueTask ApplyAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
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
        CREATE TABLE IF NOT EXISTS ano_progression_achievement_unlocks (
            player_steam_id BIGINT UNSIGNED NOT NULL,
            achievement_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
            tier SMALLINT UNSIGNED NOT NULL,
            definition_version INT UNSIGNED NOT NULL,
            reward_xp BIGINT UNSIGNED NOT NULL,
            grant_id VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
            unlocked_at_utc DATETIME(6) NOT NULL,
            PRIMARY KEY (player_steam_id, achievement_id, tier),
            UNIQUE KEY uq_ano_progression_achievement_unlock_grant (
                player_steam_id, grant_id),
            INDEX ix_ano_progression_achievement_unlock_latest (
                player_steam_id, achievement_id, tier),
            CONSTRAINT fk_ano_progression_achievement_unlock_account
                FOREIGN KEY (player_steam_id)
                REFERENCES ano_progression_accounts(player_steam_id)
                ON DELETE CASCADE
        ) ENGINE=InnoDB
        """,
    ];
}
