using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class CombatDetailSchemaMigration009 : IDatabaseMigration
{
    public long Version => 9;
    public string Name => "combat-detail-events";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_combat_weapon_fire (
                event_id CHAR(36) NOT NULL PRIMARY KEY,
                player_steam_id BIGINT UNSIGNED NOT NULL,
                occurred_at_utc DATETIME(6) NOT NULL,
                map_name VARCHAR(128) NOT NULL,
                weapon VARCHAR(64) NOT NULL,
                INDEX ix_ano_combat_fire_player (
                    player_steam_id, map_name, weapon, occurred_at_utc)
            ) ENGINE=InnoDB;

            CREATE TABLE IF NOT EXISTS ano_combat_damage (
                event_id CHAR(36) NOT NULL PRIMARY KEY,
                victim_steam_id BIGINT UNSIGNED NOT NULL,
                attacker_steam_id BIGINT UNSIGNED NULL,
                occurred_at_utc DATETIME(6) NOT NULL,
                map_name VARCHAR(128) NOT NULL,
                weapon VARCHAR(64) NOT NULL,
                hitgroup TINYINT UNSIGNED NOT NULL,
                damage_health SMALLINT UNSIGNED NOT NULL,
                damage_armor TINYINT UNSIGNED NOT NULL,
                is_team_damage BOOLEAN NOT NULL,
                INDEX ix_ano_combat_damage_attacker (
                    attacker_steam_id, map_name, weapon, hitgroup, occurred_at_utc),
                INDEX ix_ano_combat_damage_victim (
                    victim_steam_id, occurred_at_utc)
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
