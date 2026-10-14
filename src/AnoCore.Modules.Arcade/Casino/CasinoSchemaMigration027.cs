using System.Data.Common;
using AnoCore.Abstractions.Persistence;
namespace AnoCore.Modules.Arcade.Casino;

public sealed class CasinoSchemaMigration027 : IDatabaseMigration
{
    public long Version => 27;
    public string Name => "casino-ap-wallet-and-operations";
    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        foreach (var sql in new[] {
            "CREATE TABLE IF NOT EXISTS ano_ap_accounts (player_steam_id BIGINT UNSIGNED PRIMARY KEY, available BIGINT NOT NULL, allocated BIGINT NOT NULL, revision BIGINT NOT NULL, CONSTRAINT chk_ap_available CHECK (available >= 0), CONSTRAINT chk_ap_allocated CHECK (allocated >= 0)) ENGINE=InnoDB",
            "CREATE TABLE IF NOT EXISTS ano_casino_operations (operation_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY, player_steam_id BIGINT UNSIGNED NOT NULL, request_json TEXT NOT NULL, result_json TEXT NOT NULL, created_at_utc DATETIME(6) NOT NULL, INDEX ix_casino_player (player_steam_id, created_at_utc)) ENGINE=InnoDB" })
        {
            await using var command = connection.CreateCommand(); command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
