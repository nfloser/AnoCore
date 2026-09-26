using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Stats;

public sealed class MySqlRankAdjustmentRepository : IRankAdjustmentRepository
{
    public const long MaximumAbsolutePoints = 1_000_000_000;
    private readonly IDatabase _database;

    public MySqlRankAdjustmentRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public ValueTask<RankPointAdjustment?> ReadAsync(
        PlayerId playerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        return _database.WithConnectionAsync<RankPointAdjustment?>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT points, updated_by_steam_id, updated_at_utc
                FROM ano_rank_adjustments WHERE player_steam_id = @player
                """;
            Add(command, "@player", playerId.SteamId64);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
            return new RankPointAdjustment(
                playerId,
                reader.GetInt64(0),
                ReadPlayer(reader, 1),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)));
        }, cancellationToken);
    }

    public ValueTask SetAsync(PlayerId playerId, long points, PlayerId? updatedBy,
        DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        if (points is < -MaximumAbsolutePoints or > MaximumAbsolutePoints)
            throw new ArgumentOutOfRangeException(nameof(points));
        return _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ano_rank_adjustments (
                    player_steam_id, points, updated_by_steam_id, updated_at_utc)
                VALUES (@player, @points, @actor, @updated)
                ON DUPLICATE KEY UPDATE points = VALUES(points),
                    updated_by_steam_id = VALUES(updated_by_steam_id),
                    updated_at_utc = VALUES(updated_at_utc)
                """;
            Add(command, "@player", playerId.SteamId64);
            Add(command, "@points", points);
            Add(command, "@actor", updatedBy is null ? DBNull.Value : updatedBy.SteamId64);
            Add(command, "@updated", updatedAtUtc.ToUniversalTime().UtcDateTime);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public ValueTask ResetAsync(PlayerId playerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        return _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "DELETE FROM ano_rank_adjustments WHERE player_steam_id = @player";
            Add(command, "@player", playerId.SteamId64);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    private static PlayerId? ReadPlayer(DbDataReader reader, int index)
        => reader.IsDBNull(index) ? null
            : new PlayerId(Convert.ToUInt64(reader.GetValue(index), CultureInfo.InvariantCulture));
    private static void Add(DbCommand command, string name, object value)
        => MigrationRunner.AddParameter(command, name, value);
}
