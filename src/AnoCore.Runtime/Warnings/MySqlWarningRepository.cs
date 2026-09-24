using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Warnings;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Warnings;

public sealed class MySqlWarningRepository : IWarningRepository
{
    private const string Columns = """
        warning_id, target_steam_id, actor_steam_id, reason, created_at_utc,
        expires_at_utc, cleared_at_utc, cleared_by_steam_id, clear_reason
        """;
    private readonly IDatabase _database;

    public MySqlWarningRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public async ValueTask InsertAsync(WarningRecord warning, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(warning);
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ano_admin_warnings (
                    warning_id, target_steam_id, actor_steam_id, reason, created_at_utc,
                    expires_at_utc, cleared_at_utc, cleared_by_steam_id, clear_reason)
                VALUES (
                    @id, @target, @actor, @reason, @created,
                    @expires, @cleared, @clearedBy, @clearReason)
                """;
            Add(command, "@id", warning.Id.ToString("D"));
            Add(command, "@target", warning.TargetId.SteamId64);
            Add(command, "@actor", PlayerValue(warning.ActorId));
            Add(command, "@reason", warning.Reason);
            Add(command, "@created", warning.CreatedAtUtc.UtcDateTime);
            Add(command, "@expires", DateValue(warning.ExpiresAtUtc));
            Add(command, "@cleared", DateValue(warning.ClearedAtUtc));
            Add(command, "@clearedBy", PlayerValue(warning.ClearedById));
            Add(command, "@clearReason", warning.ClearReason is null ? DBNull.Value : warning.ClearReason);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<WarningRecord>> GetActiveAsync(
        PlayerId targetId, DateTimeOffset atUtc, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        WarningValidation.ValidateLimit(limit);
        return QueryAsync(targetId, atUtc.ToUniversalTime(), limit, cancellationToken);
    }

    public ValueTask<IReadOnlyList<WarningRecord>> GetHistoryAsync(
        PlayerId targetId, int limit = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        WarningValidation.ValidateLimit(limit);
        return QueryAsync(targetId, null, limit, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<WarningRecord>> ClearActiveAsync(
        PlayerId targetId, PlayerId? actorId, string reason, DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        var normalizedReason = WarningValidation.NormalizeReason(reason);
        var instant = atUtc.ToUniversalTime();
        return await _database.InTransactionAsync<IReadOnlyList<WarningRecord>>(
            async (connection, transaction, token) =>
            {
                await using var query = connection.CreateCommand();
                query.Transaction = transaction;
                query.CommandText = $"""
                    SELECT {Columns}
                    FROM ano_admin_warnings
                    WHERE target_steam_id = @target
                      AND created_at_utc <= @at
                      AND cleared_at_utc IS NULL
                      AND (expires_at_utc IS NULL OR expires_at_utc > @at)
                    ORDER BY created_at_utc, warning_id
                    FOR UPDATE
                    """;
                Add(query, "@target", targetId.SteamId64);
                Add(query, "@at", instant.UtcDateTime);
                var active = await ReadAsync(query, token).ConfigureAwait(false);
                var cleared = new List<WarningRecord>(active.Count);
                foreach (var warning in active)
                {
                    var changed = warning.Clear(actorId, normalizedReason, instant);
                    await using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = """
                        UPDATE ano_admin_warnings
                        SET cleared_at_utc = @at, cleared_by_steam_id = @actor,
                            clear_reason = @reason
                        WHERE warning_id = @id AND cleared_at_utc IS NULL
                        """;
                    Add(update, "@at", instant.UtcDateTime);
                    Add(update, "@actor", PlayerValue(actorId));
                    Add(update, "@reason", normalizedReason);
                    Add(update, "@id", warning.Id.ToString("D"));
                    if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new InvalidOperationException("Warning changed while locked for clearing.");
                    cleared.Add(changed);
                }

                return cleared;
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<IReadOnlyList<WarningRecord>> QueryAsync(
        PlayerId targetId, DateTimeOffset? atUtc, int limit, CancellationToken cancellationToken)
        => _database.WithConnectionAsync<IReadOnlyList<WarningRecord>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Columns} FROM ano_admin_warnings WHERE target_steam_id = @target"
                + (atUtc is null ? "" :
                    " AND created_at_utc <= @at"
                    + " AND (cleared_at_utc IS NULL OR cleared_at_utc > @at)"
                    + " AND (expires_at_utc IS NULL OR expires_at_utc > @at)")
                + " ORDER BY created_at_utc DESC, warning_id DESC LIMIT @limit";
            Add(command, "@target", targetId.SteamId64);
            if (atUtc is not null) Add(command, "@at", atUtc.Value.UtcDateTime);
            Add(command, "@limit", limit);
            return await ReadAsync(command, token).ConfigureAwait(false);
        }, cancellationToken);

    private static async ValueTask<IReadOnlyList<WarningRecord>> ReadAsync(
        DbCommand command, CancellationToken token)
    {
        var records = new List<WarningRecord>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            records.Add(new WarningRecord(
                ReadGuid(reader.GetValue(0)),
                ReadPlayer(reader, 1)!,
                ReadPlayer(reader, 2),
                reader.GetString(3),
                Utc(reader.GetDateTime(4)),
                ReadDate(reader, 5),
                ReadDate(reader, 6),
                ReadPlayer(reader, 7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return records;
    }

    private static DateTimeOffset Utc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static DateTimeOffset? ReadDate(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Utc(reader.GetDateTime(ordinal));
    private static PlayerId? ReadPlayer(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null
            : new PlayerId(Convert.ToUInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture));
    private static Guid ReadGuid(object value)
        => value is Guid guid ? guid : Guid.Parse(
            Convert.ToString(value, CultureInfo.InvariantCulture)
            ?? throw new InvalidDataException("Warning id could not be read."));
    private static object PlayerValue(PlayerId? value)
        => value is null ? DBNull.Value : value.SteamId64;
    private static object DateValue(DateTimeOffset? value)
        => value is null ? DBNull.Value : value.Value.UtcDateTime;
    private static void Add(DbCommand command, string name, object value)
        => MigrationRunner.AddParameter(command, name, value);
}
