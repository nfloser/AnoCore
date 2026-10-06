using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class ProgressionSeasonSchemaMigration014 : IDatabaseMigration
{
    public long Version => 14;

    public string Name => "progression-season-lifecycle";

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
        CREATE TABLE IF NOT EXISTS ano_progression_seasons (
            season_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
            definition_version INT UNSIGNED NOT NULL,
            name VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
            starts_at_utc DATETIME(6) NOT NULL,
            ends_at_utc DATETIME(6) NOT NULL,
            accepted_at_utc DATETIME(6) NOT NULL,
            closed_at_utc DATETIME(6) NULL,
            PRIMARY KEY (season_id, definition_version),
            INDEX ix_ano_progression_seasons_window (
                starts_at_utc, ends_at_utc, season_id),
            INDEX ix_ano_progression_seasons_version (
                season_id, definition_version)
        ) ENGINE=InnoDB
        """,
        """
        CREATE TABLE IF NOT EXISTS ano_progression_season_runtime (
            singleton_id TINYINT UNSIGNED NOT NULL PRIMARY KEY
        ) ENGINE=InnoDB
        """,
        """
        INSERT INTO ano_progression_season_runtime (singleton_id)
        VALUES (1)
        ON DUPLICATE KEY UPDATE singleton_id = singleton_id
        """,
    ];
}
