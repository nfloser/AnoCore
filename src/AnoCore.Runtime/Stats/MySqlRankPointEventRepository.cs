using System.Data.Common;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Stats;

public sealed class MySqlRankPointEventRepository : IRankPointEventRepository
{
    private readonly IDatabase _database;

    public MySqlRankPointEventRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public ValueTask<bool> RecordAsync(
        RankPointEvent pointEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pointEvent);
        return _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT IGNORE INTO ano_rank_point_events (
                    source_event_id, player_steam_id, component, points,
                    occurred_at_utc, map_name)
                VALUES (@event, @player, @component, @points, @occurred, @map)
                """;
            Add(command, "@event", pointEvent.SourceEventId.ToString("D"));
            Add(command, "@player", pointEvent.PlayerId.SteamId64);
            Add(command, "@component", pointEvent.Component);
            Add(command, "@points", pointEvent.Points);
            Add(command, "@occurred", pointEvent.OccurredAtUtc.UtcDateTime);
            Add(command, "@map", pointEvent.MapName);
            if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1)
                return true;

            await using var replay = connection.CreateCommand();
            replay.CommandText = """
                SELECT points, occurred_at_utc, map_name
                FROM ano_rank_point_events
                WHERE source_event_id = @event
                  AND player_steam_id = @player
                  AND component = @component
                """;
            Add(replay, "@event", pointEvent.SourceEventId.ToString("D"));
            Add(replay, "@player", pointEvent.PlayerId.SteamId64);
            Add(replay, "@component", pointEvent.Component);
            await using var reader = await replay.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                throw new InvalidOperationException("Rank point event disappeared after duplicate insert.");

            var occurred = new DateTimeOffset(
                DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));
            if (reader.GetInt64(0) != pointEvent.Points
                || occurred != pointEvent.OccurredAtUtc
                || !string.Equals(reader.GetString(2), pointEvent.MapName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Rank point event replay conflicts with the persisted event.");
            }

            return false;
        }, cancellationToken);
    }

    private static void Add(DbCommand command, string name, object value)
        => MigrationRunner.AddParameter(command, name, value);
}
