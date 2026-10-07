using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Stats;

public sealed class MySqlRankPointEventRepository : IRankPointEventRepository
{
    private readonly IDatabase _database;

    public MySqlRankPointEventRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public ValueTask<RankPointEventResult> ApplyAsync(RankPointEventBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var payload = batch.Source + "|" + batch.OccurredAtUtc.Ticks.ToString(CultureInfo.InvariantCulture)
            + "|" + string.Join("|", batch.Awards.Select(award => FormattableString.Invariant(
                $"{award.PlayerId.SteamId64}:{award.Points}")));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await using (var seed = connection.CreateCommand())
            {
                seed.Transaction = transaction;
                seed.CommandText = """
                    INSERT INTO ano_rank_point_batches (event_id, source, occurred_at_utc, payload_hash)
                    VALUES (@event, @source, @occurred, @hash)
                    ON DUPLICATE KEY UPDATE event_id = event_id
                    """;
                Add(seed, "@event", batch.EventId.ToString("D"));
                Add(seed, "@source", batch.Source);
                Add(seed, "@occurred", batch.OccurredAtUtc.UtcDateTime);
                Add(seed, "@hash", hash);
                await seed.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            await using (var locked = connection.CreateCommand())
            {
                locked.Transaction = transaction;
                locked.CommandText = "SELECT payload_hash FROM ano_rank_point_batches WHERE event_id = @event FOR UPDATE";
                Add(locked, "@event", batch.EventId.ToString("D"));
                var storedHash = Convert.ToString(await locked.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (!string.Equals(hash, storedHash, StringComparison.Ordinal))
                    throw new InvalidOperationException("Rank event identity was reused with a different payload.");
            }
            var existing = await ReadAwardsAsync(connection, transaction, batch.EventId, token).ConfigureAwait(false);
            if (existing.Count > 0)
            {
                if (!existing.SequenceEqual(batch.Awards))
                    throw new InvalidOperationException("Stored rank event awards do not match their batch.");
                return new RankPointEventResult(false, batch);
            }
            foreach (var award in batch.Awards)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO ano_rank_point_events (event_id, player_steam_id, points)
                    VALUES (@event, @player, @points)
                    """;
                Add(command, "@event", batch.EventId.ToString("D"));
                Add(command, "@player", award.PlayerId.SteamId64);
                Add(command, "@points", award.Points);
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            return new RankPointEventResult(true, batch);
        }, IsolationLevel.ReadCommitted, cancellationToken);
    }

    public ValueTask<RankPointEventBatch?> ReadAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("A rank event ID is required.", nameof(eventId));
        return _database.WithConnectionAsync<RankPointEventBatch?>(async (connection, token) =>
        {
            string source;
            DateTimeOffset occurred;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT source, occurred_at_utc FROM ano_rank_point_batches WHERE event_id = @event";
                Add(command, "@event", eventId.ToString("D"));
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
                source = reader.GetString(0);
                occurred = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));
            }
            var awards = await ReadAwardsAsync(connection, null, eventId, token).ConfigureAwait(false);
            return RankPointEventBatch.Create(eventId, source, occurred, awards);
        }, cancellationToken);
    }

    private static async ValueTask<IReadOnlyList<RankPointAward>> ReadAwardsAsync(DbConnection connection,
        DbTransaction? transaction, Guid eventId, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT player_steam_id, points FROM ano_rank_point_events
            WHERE event_id = @event ORDER BY player_steam_id LIMIT 65
            """;
        Add(command, "@event", eventId.ToString("D"));
        var awards = new List<RankPointAward>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            awards.Add(new RankPointAward(new PlayerId(Convert.ToUInt64(reader.GetValue(0), CultureInfo.InvariantCulture)), reader.GetInt64(1)));
        return awards.AsReadOnly();
    }

    private static void Add(DbCommand command, string name, object value)
        => MigrationRunner.AddParameter(command, name, value);
}
