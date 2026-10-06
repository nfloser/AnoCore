using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class ProgressionSchemaMigration013 : IDatabaseMigration
{
    public long Version => 13;

    public string Name => "progression-lifetime-xp";

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
        CREATE TABLE IF NOT EXISTS ano_progression_accounts (
            player_steam_id BIGINT UNSIGNED NOT NULL PRIMARY KEY,
            lifetime_xp BIGINT UNSIGNED NOT NULL,
            revision BIGINT UNSIGNED NOT NULL,
            updated_at_utc DATETIME(6) NOT NULL
        ) ENGINE=InnoDB
        """,
        """
        CREATE TABLE IF NOT EXISTS ano_progression_grants (
            player_steam_id BIGINT UNSIGNED NOT NULL,
            grant_id VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
            source TINYINT UNSIGNED NOT NULL,
            base_xp BIGINT UNSIGNED NOT NULL,
            awarded_xp BIGINT UNSIGNED NOT NULL,
            reason VARCHAR(128) NOT NULL,
            occurred_at_utc DATETIME(6) NOT NULL,
            boost_id VARCHAR(64) NULL,
            boost_multiplier DECIMAL(6,3) NOT NULL,
            lifetime_xp_after BIGINT UNSIGNED NOT NULL,
            account_revision_after BIGINT UNSIGNED NOT NULL,
            created_at_utc DATETIME(6) NOT NULL,
            PRIMARY KEY (player_steam_id, grant_id),
            INDEX ix_ano_progression_grants_occurred (
                player_steam_id, occurred_at_utc, grant_id),
            CONSTRAINT fk_ano_progression_grants_account
                FOREIGN KEY (player_steam_id)
                REFERENCES ano_progression_accounts(player_steam_id)
                ON DELETE CASCADE
        ) ENGINE=InnoDB
        """,
    ];
}
