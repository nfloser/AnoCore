using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Arcade.Casino;

/// <summary>
/// Reconciles the physical Casino tables with the migration history before live presenters are registered.
/// Migrations 027/028 are CREATE IF NOT EXISTS-only, so replaying them is safe and repairs partial test deployments.
/// </summary>
public static class CasinoPersistenceBootstrap
{
    public static ValueTask<bool> EnsureReadyAsync(
        IDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        return database.WithConnectionAsync(async (connection, token) =>
        {
            await new CasinoSchemaMigration027().ApplyAsync(connection, token).ConfigureAwait(false);
            await new CasinoSchemaMigration028().ApplyAsync(connection, token).ConfigureAwait(false);
            await VerifyAsync(connection, token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    private static async ValueTask VerifyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        foreach (var sql in new[]
        {
            "SELECT player_steam_id, available, allocated, revision FROM ano_ap_accounts LIMIT 0",
            "SELECT operation_id, player_steam_id, request_json, result_json, created_at_utc FROM ano_casino_operations LIMIT 0",
            "SELECT round_id, player_steam_id, game_kind, rules_version, state_json, status, revision, reserved_payout, active_player_steam_id FROM ano_casino_rounds LIMIT 0",
            "SELECT operation_id, round_id, player_steam_id, expected_revision, request_json, result_json, resulting_revision, created_at_utc FROM ano_casino_round_actions LIMIT 0",
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
