using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Durable multi-action rounds and their idempotent action ledger.</summary>
public sealed class CasinoSchemaMigration028 : IDatabaseMigration
{
    public long Version => 28;
    public string Name => "casino-durable-rounds";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        foreach (var sql in new[]
        {
            """
            CREATE TABLE IF NOT EXISTS ano_casino_rounds (
                round_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
                player_steam_id BIGINT UNSIGNED NOT NULL,
                game_kind TINYINT UNSIGNED NOT NULL,
                rules_version VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                state_json MEDIUMTEXT NOT NULL,
                status VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                revision BIGINT NOT NULL,
                reserved_payout BIGINT NOT NULL,
                created_at_utc DATETIME(6) NOT NULL,
                updated_at_utc DATETIME(6) NOT NULL,
                active_player_steam_id BIGINT UNSIGNED AS (
                    CASE WHEN status = 'active' THEN player_steam_id ELSE NULL END
                ) STORED,
                UNIQUE KEY uq_casino_active_player (active_player_steam_id),
                INDEX ix_casino_round_player (player_steam_id, updated_at_utc),
                CONSTRAINT chk_casino_round_status CHECK (status IN ('active', 'completed')),
                CONSTRAINT chk_casino_round_revision CHECK (revision >= 0),
                CONSTRAINT chk_casino_round_reserve CHECK (reserved_payout >= 0)
            ) ENGINE=InnoDB
            """,
            """
            CREATE TABLE IF NOT EXISTS ano_casino_round_actions (
                operation_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
                round_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                player_steam_id BIGINT UNSIGNED NOT NULL,
                expected_revision BIGINT NOT NULL,
                request_json TEXT NOT NULL,
                result_json MEDIUMTEXT NOT NULL,
                resulting_revision BIGINT NOT NULL,
                created_at_utc DATETIME(6) NOT NULL,
                UNIQUE KEY uq_casino_round_revision (round_id, resulting_revision),
                INDEX ix_casino_round_action_player (player_steam_id, created_at_utc),
                CONSTRAINT fk_casino_round_action_round FOREIGN KEY (round_id)
                    REFERENCES ano_casino_rounds (round_id),
                CONSTRAINT chk_casino_action_revision CHECK (
                    expected_revision >= 0 AND resulting_revision = expected_revision + 1)
            ) ENGINE=InnoDB
            """,
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
