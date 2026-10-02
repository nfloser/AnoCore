using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class CombatSchemaMigration006 : IDatabaseMigration
{
    public long Version => 6;
    public string Name => "combat-events";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_combat_deaths (
                event_id CHAR(36) NOT NULL PRIMARY KEY,
                victim_steam_id BIGINT UNSIGNED NOT NULL,
                attacker_steam_id BIGINT UNSIGNED NULL,
                assister_steam_id BIGINT UNSIGNED NULL,
                occurred_at_utc DATETIME(6) NOT NULL,
                is_team_kill BOOLEAN NOT NULL,
                INDEX ix_ano_combat_victim (victim_steam_id),
                INDEX ix_ano_combat_attacker (attacker_steam_id, is_team_kill),
                INDEX ix_ano_combat_assister (assister_steam_id)
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
