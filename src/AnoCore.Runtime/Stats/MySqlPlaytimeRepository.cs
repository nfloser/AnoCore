using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Stats;

public sealed class MySqlPlaytimeRepository : IPlaytimeRepository
{
    private readonly IDatabase _database;

    public MySqlPlaytimeRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public async ValueTask OpenAsync(PlayerId playerId, PlayerSessionId sessionId,
        DateTimeOffset startedAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(sessionId);
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ano_playtime_sessions (
                    session_id, steam_id, started_at_utc, accounted_until_utc)
                VALUES (@session, @player, @started, @started)
                ON DUPLICATE KEY UPDATE session_id = session_id
                """;
            Add(command, "@session", sessionId.ToString());
            Add(command, "@player", playerId.SteamId64);
            Add(command, "@started", startedAtUtc.UtcDateTime);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask AdvanceAsync(PlayerId playerId, PlayerSessionId sessionId,
        DateTimeOffset atUtc, bool close = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(sessionId);
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE ano_playtime_sessions
                SET closed_at_utc = CASE WHEN @close = 1
                        THEN GREATEST(accounted_until_utc, started_at_utc, @at)
                        ELSE NULL END,
                    accounted_until_utc = GREATEST(accounted_until_utc, started_at_utc, @at)
                WHERE session_id = @session AND steam_id = @player AND closed_at_utc IS NULL
                """;
            Add(command, "@session", sessionId.ToString());
            Add(command, "@player", playerId.SteamId64);
            Add(command, "@at", atUtc.UtcDateTime);
            Add(command, "@close", close ? 1 : 0);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<PlaytimeTotals> ReadAsync(PlayerId playerId, DateOnly utcDay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        var day = new DateTimeOffset(utcDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var tomorrow = day.AddDays(1);
        return _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    COALESCE(SUM(TIMESTAMPDIFF(MICROSECOND, started_at_utc, accounted_until_utc)), 0),
                    COALESCE(SUM(CASE
                        WHEN started_at_utc < @tomorrow AND accounted_until_utc > @day
                        THEN TIMESTAMPDIFF(MICROSECOND,
                            GREATEST(started_at_utc, @day),
                            LEAST(accounted_until_utc, @tomorrow))
                        ELSE 0 END), 0)
                FROM ano_playtime_sessions WHERE steam_id = @player
                """;
            Add(command, "@player", playerId.SteamId64);
            Add(command, "@day", day.UtcDateTime);
            Add(command, "@tomorrow", tomorrow.UtcDateTime);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                throw new InvalidOperationException("Playtime aggregate query returned no row.");
            return new PlaytimeTotals(
                TimeSpan.FromTicks(checked(Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture) * 10)),
                TimeSpan.FromTicks(checked(Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture) * 10)));
        }, cancellationToken);
    }

    private static void Add(DbCommand command, string name, object value)
        => MigrationRunner.AddParameter(command, name, value);
}
