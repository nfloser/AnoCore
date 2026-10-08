using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class CombatDeathContextSchemaMigration020 : IDatabaseMigration
{
    public long Version => 20;
    public string Name => "combat-death-context";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ano_combat_death_context (
                event_id CHAR(36) NOT NULL PRIMARY KEY,
                map_name VARCHAR(128) NOT NULL,
                weapon VARCHAR(64) NOT NULL,
                attacker_team TINYINT UNSIGNED NOT NULL,
                headshot BOOLEAN NOT NULL,
                no_scope BOOLEAN NOT NULL,
                through_smoke BOOLEAN NOT NULL,
                penetrations TINYINT UNSIGNED NOT NULL,
                distance_meters DECIMAL(9,4) NULL,
                attacker_blind BOOLEAN NOT NULL
            ) ENGINE=InnoDB
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
