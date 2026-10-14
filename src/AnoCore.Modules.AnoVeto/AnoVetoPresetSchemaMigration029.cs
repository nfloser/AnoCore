using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.AnoVeto;

/// <summary>Owner locks make the per-user preset limit safe across CS2 instances.</summary>
public sealed class AnoVetoPresetSchemaMigration029 : IDatabaseMigration
{
    public long Version => 29;
    public string Name => "anoveto-personal-presets";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        foreach (var statement in Statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static readonly string[] Statements =
    [
        """
        CREATE TABLE IF NOT EXISTS ano_veto_preset_owners (
            owner_steamid VARCHAR(20) NOT NULL PRIMARY KEY
        ) ENGINE=InnoDB
        """,
        """
        CREATE TABLE IF NOT EXISTS ano_veto_presets (
            owner_steamid VARCHAR(20) NOT NULL,
            name_key VARCHAR(48) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
            display_name VARCHAR(48) NOT NULL,
            map_ids_json LONGTEXT NOT NULL,
            revision BIGINT NOT NULL,
            updated_at_utc DATETIME(6) NOT NULL,
            PRIMARY KEY (owner_steamid, name_key),
            CONSTRAINT fk_ano_veto_preset_owner
                FOREIGN KEY (owner_steamid) REFERENCES ano_veto_preset_owners(owner_steamid)
                ON DELETE CASCADE
        ) ENGINE=InnoDB
        """
    ];
}
