using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class CoreSchemaMigration001 : IDatabaseMigration
{
    public long Version => 1;

    public string Name => "core-player-and-module-data";

    public async ValueTask ApplyAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS ano_players (
                steam_id BIGINT UNSIGNED NOT NULL PRIMARY KEY,
                last_known_name VARCHAR(128) NOT NULL,
                first_seen_utc DATETIME(6) NOT NULL,
                last_seen_utc DATETIME(6) NOT NULL,
                INDEX ix_ano_players_last_seen (last_seen_utc)
            ) ENGINE=InnoDB
            """, cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS ano_module_data (
                module_id VARCHAR(64) NOT NULL,
                data_key VARCHAR(128) NOT NULL,
                data_json LONGTEXT NOT NULL,
                updated_at_utc DATETIME(6) NOT NULL,
                PRIMARY KEY (module_id, data_key)
            ) ENGINE=InnoDB
            """, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
