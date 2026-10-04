using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class StatisticsResetSchemaMigration011 : IDatabaseMigration
{
    public long Version => 11;
    public string Name => "statistics-reset-cutoff";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using (var reset = connection.CreateCommand())
        {
            reset.CommandText = """
                CREATE TABLE IF NOT EXISTS ano_statistics_resets (
                    player_steam_id BIGINT UNSIGNED NOT NULL PRIMARY KEY,
                    reset_at_utc DATETIME(6) NOT NULL,
                    updated_by_steam_id BIGINT UNSIGNED NULL,
                    INDEX ix_ano_statistics_resets_time (reset_at_utc)
                ) ENGINE=InnoDB
                """;
            await reset.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var sql in Views)
        {
            await using var view = connection.CreateCommand();
            view.CommandText = sql;
            await view.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static readonly string[] Views =
    [
        """
        CREATE OR REPLACE VIEW ano_effective_combat_kills AS
        SELECT d.*
        FROM ano_combat_deaths AS d
        LEFT JOIN ano_statistics_resets AS r
            ON r.player_steam_id = d.attacker_steam_id
        WHERE d.attacker_steam_id IS NOT NULL
          AND d.is_team_kill = 0
          AND (r.reset_at_utc IS NULL OR d.occurred_at_utc > r.reset_at_utc)
        """,
        """
        CREATE OR REPLACE VIEW ano_effective_combat_deaths AS
        SELECT d.*
        FROM ano_combat_deaths AS d
        LEFT JOIN ano_statistics_resets AS r
            ON r.player_steam_id = d.victim_steam_id
        WHERE r.reset_at_utc IS NULL OR d.occurred_at_utc > r.reset_at_utc
        """,
        """
        CREATE OR REPLACE VIEW ano_effective_combat_assists AS
        SELECT d.*
        FROM ano_combat_deaths AS d
        LEFT JOIN ano_statistics_resets AS r
            ON r.player_steam_id = d.assister_steam_id
        WHERE d.assister_steam_id IS NOT NULL
          AND (r.reset_at_utc IS NULL OR d.occurred_at_utc > r.reset_at_utc)
        """,
        """
        CREATE OR REPLACE VIEW ano_effective_combat_weapon_fire AS
        SELECT f.*
        FROM ano_combat_weapon_fire AS f
        LEFT JOIN ano_statistics_resets AS r
            ON r.player_steam_id = f.player_steam_id
        WHERE r.reset_at_utc IS NULL OR f.occurred_at_utc > r.reset_at_utc
        """,
        """
        CREATE OR REPLACE VIEW ano_effective_combat_damage AS
        SELECT d.*
        FROM ano_combat_damage AS d
        LEFT JOIN ano_statistics_resets AS r
            ON r.player_steam_id = d.attacker_steam_id
        WHERE d.attacker_steam_id IS NOT NULL
          AND (r.reset_at_utc IS NULL OR d.occurred_at_utc > r.reset_at_utc)
        """,
        """
        CREATE OR REPLACE VIEW ano_effective_gameplay_stats AS
        SELECT g.*
        FROM ano_gameplay_stats AS g
        LEFT JOIN ano_statistics_resets AS r
            ON r.player_steam_id = g.player_steam_id
        WHERE r.reset_at_utc IS NULL OR g.occurred_at_utc > r.reset_at_utc
        """,
    ];
}
