using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class ProgressionSeasonXpSchemaMigration015 : IDatabaseMigration
{
    public long Version => 15;

    public string Name => "progression-season-xp";

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
        CREATE TABLE IF NOT EXISTS ano_progression_season_accounts (
            player_steam_id BIGINT UNSIGNED NOT NULL,
            season_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
            season_version INT UNSIGNED NOT NULL,
            season_xp BIGINT UNSIGNED NOT NULL,
            revision BIGINT UNSIGNED NOT NULL,
            updated_at_utc DATETIME(6) NOT NULL,
            PRIMARY KEY (player_steam_id, season_id),
            INDEX ix_ano_progression_season_accounts_season (
                season_id, season_version, season_xp, player_steam_id),
            CONSTRAINT fk_ano_progression_season_accounts_definition
                FOREIGN KEY (season_id, season_version)
                REFERENCES ano_progression_seasons(season_id, definition_version)
        ) ENGINE=InnoDB
        """,
        """
        CREATE TABLE IF NOT EXISTS ano_progression_season_grants (
            player_steam_id BIGINT UNSIGNED NOT NULL,
            season_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
            season_version INT UNSIGNED NOT NULL,
            grant_id VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
            source TINYINT UNSIGNED NOT NULL,
            base_xp BIGINT NOT NULL,
            awarded_xp BIGINT NOT NULL,
            reason VARCHAR(128) NOT NULL,
            occurred_at_utc DATETIME(6) NOT NULL,
            boost_id VARCHAR(64) NULL,
            boost_multiplier DECIMAL(30,28) NOT NULL,
            season_xp_after BIGINT UNSIGNED NOT NULL,
            account_revision_after BIGINT UNSIGNED NOT NULL,
            created_at_utc DATETIME(6) NOT NULL,
            PRIMARY KEY (player_steam_id, season_id, grant_id),
            INDEX ix_ano_progression_season_grants_occurred (
                season_id, player_steam_id, occurred_at_utc, grant_id),
            CONSTRAINT fk_ano_progression_season_grants_account
                FOREIGN KEY (player_steam_id, season_id)
                REFERENCES ano_progression_season_accounts(player_steam_id, season_id)
                ON DELETE CASCADE,
            CONSTRAINT fk_ano_progression_season_grants_definition
                FOREIGN KEY (season_id, season_version)
                REFERENCES ano_progression_seasons(season_id, definition_version)
        ) ENGINE=InnoDB
        """,
    ];
}
