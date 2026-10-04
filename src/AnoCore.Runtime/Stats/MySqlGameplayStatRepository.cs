using System.Data.Common;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Runtime.Stats;

public sealed class MySqlGameplayStatRepository : IGameplayStatRepository
{
    private readonly IDatabase _database;

    public MySqlGameplayStatRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public async ValueTask RecordAsync(GameplayStatEvent statistic,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statistic);
        await _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO ano_gameplay_stats (
                        event_id, player_steam_id, occurred_at_utc, map_name, stat_kind, amount)
                    VALUES (@id, @player, @occurred, @map, @kind, @amount)
                    ON DUPLICATE KEY UPDATE event_id = event_id
                    """;
                Add(insert, "@id", statistic.EventId.ToString("D"));
                Add(insert, "@player", statistic.PlayerId.SteamId64);
                Add(insert, "@occurred", statistic.OccurredAtUtc.UtcDateTime);
                Add(insert, "@map", statistic.MapName);
                Add(insert, "@kind", (byte)statistic.Kind);
                Add(insert, "@amount", statistic.Amount);
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            await using var verify = connection.CreateCommand();
            verify.Transaction = transaction;
            verify.CommandText = """
                SELECT player_steam_id, occurred_at_utc, map_name, stat_kind, amount
                FROM ano_gameplay_stats
                WHERE event_id = @id
                FOR UPDATE
                """;
            Add(verify, "@id", statistic.EventId.ToString("D"));
            await using var reader = await verify.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)
                || Convert.ToUInt64(reader.GetValue(0)) != statistic.PlayerId.SteamId64
                || ReadUtc(reader, 1) != statistic.OccurredAtUtc
                || !string.Equals(reader.GetString(2), statistic.MapName, StringComparison.Ordinal)
                || Convert.ToByte(reader.GetValue(3)) != (byte)statistic.Kind
                || Convert.ToInt32(reader.GetValue(4)) != statistic.Amount)
            {
                throw new InvalidOperationException(
                    "Gameplay statistic event id conflicts with a different event.");
            }

            return true;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<GameplayStatTotal>> ReadAsync(PlayerId playerId,
        GameplayStatFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        filter ??= new GameplayStatFilter();
        return _database.WithConnectionAsync<IReadOnlyList<GameplayStatTotal>>(
            async (connection, token) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT stat_kind, COALESCE(SUM(amount), 0)
                    FROM ano_gameplay_stats
                    WHERE player_steam_id = @player
                      AND (@map IS NULL OR map_name = @map)
                    GROUP BY stat_kind
                    ORDER BY stat_kind ASC
                    """;
                Add(command, "@player", playerId.SteamId64);
                Add(command, "@map", filter.MapName);

                var result = new List<GameplayStatTotal>();
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    var kind = (GameplayStatKind)Convert.ToByte(reader.GetValue(0));
                    if (!Enum.IsDefined(kind))
                        throw new InvalidOperationException(
                            $"Stored gameplay statistic kind {(byte)kind} is unsupported.");
                    result.Add(new GameplayStatTotal(
                        kind, Convert.ToInt64(reader.GetValue(1))));
                }

                return result;
            }, cancellationToken);
    }

    private static DateTimeOffset ReadUtc(DbDataReader reader, int ordinal)
    {
        var value = reader.GetDateTime(ordinal);
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
