using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Stats;

public sealed class MySqlPlaytimeRepository : IPlaytimeStateRepository
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

    public async ValueTask OpenStateAsync(PlayerId playerId, PlayerSessionId sessionId,
        DateTimeOffset startedAtUtc, PlaytimeState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(state);

        await _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await using (var open = connection.CreateCommand())
            {
                open.Transaction = transaction;
                open.CommandText = """
                    INSERT INTO ano_playtime_sessions (
                        session_id, steam_id, started_at_utc, accounted_until_utc)
                    VALUES (@session, @player, @started, @started)
                    ON DUPLICATE KEY UPDATE session_id = session_id
                    """;
                Add(open, "@session", sessionId.ToString());
                Add(open, "@player", playerId.SteamId64);
                Add(open, "@started", startedAtUtc.UtcDateTime);
                await open.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            var checkpoint = await ReadSessionCheckpointAsync(
                connection, transaction, playerId, sessionId, token).ConfigureAwait(false);
            if (checkpoint is null || checkpoint.Value.ClosedAtUtc is not null)
                return true;

            var current = await ReadOpenSegmentAsync(
                connection, transaction, playerId, sessionId, token).ConfigureAwait(false);
            if (current is null)
            {
                await InsertSegmentAsync(connection, transaction, playerId, sessionId,
                    checkpoint.Value.AccountedUntilUtc, state, token).ConfigureAwait(false);
            }
            else if (current.Value.State != state)
            {
                await CloseSegmentAsync(connection, transaction, current.Value.Id,
                    checkpoint.Value.AccountedUntilUtc, token).ConfigureAwait(false);
                await InsertSegmentAsync(connection, transaction, playerId, sessionId,
                    checkpoint.Value.AccountedUntilUtc, state, token).ConfigureAwait(false);
            }

            return true;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask AdvanceStateAsync(PlayerId playerId, PlayerSessionId sessionId,
        DateTimeOffset atUtc, PlaytimeState stateAtUtc, bool close = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(stateAtUtc);
        var at = atUtc.ToUniversalTime();

        await _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            var checkpoint = await ReadSessionCheckpointAsync(
                connection, transaction, playerId, sessionId, token).ConfigureAwait(false);
            if (checkpoint is null || checkpoint.Value.ClosedAtUtc is not null)
                return true;
            if (at < checkpoint.Value.AccountedUntilUtc)
                return true;

            var current = await ReadOpenSegmentAsync(
                connection, transaction, playerId, sessionId, token).ConfigureAwait(false);

            await using (var session = connection.CreateCommand())
            {
                session.Transaction = transaction;
                session.CommandText = """
                    UPDATE ano_playtime_sessions
                    SET accounted_until_utc = @at,
                        closed_at_utc = CASE WHEN @close = 1 THEN @at ELSE NULL END
                    WHERE session_id = @session AND steam_id = @player AND closed_at_utc IS NULL
                    """;
                Add(session, "@at", at.UtcDateTime);
                Add(session, "@close", close ? 1 : 0);
                Add(session, "@session", sessionId.ToString());
                Add(session, "@player", playerId.SteamId64);
                await session.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            if (current is null)
            {
                if (!close)
                    await InsertSegmentAsync(connection, transaction, playerId, sessionId,
                        at, stateAtUtc, token).ConfigureAwait(false);
                return true;
            }

            if (close)
            {
                await CloseSegmentAsync(connection, transaction, current.Value.Id, at, token)
                    .ConfigureAwait(false);
                return true;
            }

            if (current.Value.State == stateAtUtc)
            {
                await AdvanceSegmentAsync(connection, transaction, current.Value.Id, at, token)
                    .ConfigureAwait(false);
                return true;
            }

            await CloseSegmentAsync(connection, transaction, current.Value.Id, at, token)
                .ConfigureAwait(false);
            await InsertSegmentAsync(connection, transaction, playerId, sessionId,
                at, stateAtUtc, token).ConfigureAwait(false);
            return true;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
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
                Micros(reader.GetValue(0)),
                Micros(reader.GetValue(1)));
        }, cancellationToken);
    }

    public ValueTask<IReadOnlyList<PlaytimeStateBreakdown>> ReadStateBreakdownAsync(
        PlayerId playerId, DateOnly utcDay, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        var day = new DateTimeOffset(utcDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var tomorrow = day.AddDays(1);
        return _database.WithConnectionAsync<IReadOnlyList<PlaytimeStateBreakdown>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT team, is_alive,
                    COALESCE(SUM(TIMESTAMPDIFF(MICROSECOND, started_at_utc, accounted_until_utc)), 0),
                    COALESCE(SUM(CASE
                        WHEN started_at_utc < @tomorrow AND accounted_until_utc > @day
                        THEN TIMESTAMPDIFF(MICROSECOND,
                            GREATEST(started_at_utc, @day),
                            LEAST(accounted_until_utc, @tomorrow))
                        ELSE 0 END), 0)
                FROM ano_playtime_segments
                WHERE steam_id = @player
                GROUP BY team, is_alive
                ORDER BY team, is_alive DESC
                """;
            Add(command, "@player", playerId.SteamId64);
            Add(command, "@day", day.UtcDateTime);
            Add(command, "@tomorrow", tomorrow.UtcDateTime);
            var result = new List<PlaytimeStateBreakdown>();
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                result.Add(new PlaytimeStateBreakdown(
                    (PlayerTeam)Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                    Convert.ToBoolean(reader.GetValue(1), CultureInfo.InvariantCulture),
                    Micros(reader.GetValue(2)),
                    Micros(reader.GetValue(3))));
            }
            return result;
        }, cancellationToken);
    }

    public ValueTask<IReadOnlyList<PlaytimeRankEntry>> GetTopAsync(int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(offset));
        return _database.WithConnectionAsync<IReadOnlyList<PlaytimeRankEntry>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT ranked.steam_id, ranked.total_microseconds, profiles.last_known_name
                FROM (
                    SELECT steam_id,
                        SUM(TIMESTAMPDIFF(MICROSECOND, started_at_utc, accounted_until_utc))
                            AS total_microseconds
                    FROM ano_playtime_sessions
                    GROUP BY steam_id
                    HAVING total_microseconds > 0
                ) AS ranked
                LEFT JOIN ano_players AS profiles ON profiles.steam_id = ranked.steam_id
                ORDER BY ranked.total_microseconds DESC, ranked.steam_id ASC
                LIMIT @limit OFFSET @offset
                """;
            Add(command, "@limit", limit);
            Add(command, "@offset", offset);
            var entries = new List<PlaytimeRankEntry>();
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var id = new PlayerId(Convert.ToUInt64(reader.GetValue(0), CultureInfo.InvariantCulture));
                var microseconds = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
                entries.Add(new PlaytimeRankEntry(id,
                    TimeSpan.FromTicks(checked(microseconds * 10)), offset + entries.Count + 1,
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
            return entries;
        }, cancellationToken);
    }

    private static async ValueTask<(DateTimeOffset AccountedUntilUtc, DateTimeOffset? ClosedAtUtc)?>
        ReadSessionCheckpointAsync(DbConnection connection, DbTransaction transaction,
            PlayerId playerId, PlayerSessionId sessionId, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT accounted_until_utc, closed_at_utc
            FROM ano_playtime_sessions
            WHERE session_id = @session AND steam_id = @player
            FOR UPDATE
            """;
        Add(command, "@session", sessionId.ToString());
        Add(command, "@player", playerId.SteamId64);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        var accounted = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc));
        DateTimeOffset? closed = reader.IsDBNull(1)
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));
        return (accounted, closed);
    }

    private static async ValueTask<(long Id, PlaytimeState State)?> ReadOpenSegmentAsync(
        DbConnection connection, DbTransaction transaction, PlayerId playerId,
        PlayerSessionId sessionId, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT segment_id, team, is_alive
            FROM ano_playtime_segments
            WHERE session_id = @session AND steam_id = @player AND closed_at_utc IS NULL
            ORDER BY segment_id DESC
            LIMIT 1
            FOR UPDATE
            """;
        Add(command, "@session", sessionId.ToString());
        Add(command, "@player", playerId.SteamId64);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        return (
            reader.GetInt64(0),
            new PlaytimeState((PlayerTeam)reader.GetInt32(1), reader.GetBoolean(2)));
    }

    private static async ValueTask InsertSegmentAsync(DbConnection connection,
        DbTransaction transaction, PlayerId playerId, PlayerSessionId sessionId,
        DateTimeOffset atUtc, PlaytimeState state, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_playtime_segments (
                session_id, steam_id, team, is_alive, started_at_utc, accounted_until_utc)
            VALUES (@session, @player, @team, @alive, @at, @at)
            """;
        Add(command, "@session", sessionId.ToString());
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@team", (int)state.Team);
        Add(command, "@alive", state.IsAlive ? 1 : 0);
        Add(command, "@at", atUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async ValueTask AdvanceSegmentAsync(DbConnection connection,
        DbTransaction transaction, long segmentId, DateTimeOffset atUtc, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ano_playtime_segments
            SET accounted_until_utc = GREATEST(accounted_until_utc, started_at_utc, @at)
            WHERE segment_id = @id AND closed_at_utc IS NULL
            """;
        Add(command, "@id", segmentId);
        Add(command, "@at", atUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async ValueTask CloseSegmentAsync(DbConnection connection,
        DbTransaction transaction, long segmentId, DateTimeOffset atUtc, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ano_playtime_segments
            SET accounted_until_utc = GREATEST(accounted_until_utc, started_at_utc, @at),
                closed_at_utc = GREATEST(accounted_until_utc, started_at_utc, @at)
            WHERE segment_id = @id AND closed_at_utc IS NULL
            """;
        Add(command, "@id", segmentId);
        Add(command, "@at", atUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static TimeSpan Micros(object value)
        => TimeSpan.FromTicks(checked(Convert.ToInt64(value, CultureInfo.InvariantCulture) * 10));

    private static void Add(DbCommand command, string name, object value)
        => MigrationRunner.AddParameter(command, name, value);
}
