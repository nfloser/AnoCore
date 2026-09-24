using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Auditing;

public sealed class MySqlAdminAuditRepository : IAdminAuditRepository
{
    private readonly IDatabase _database;

    public MySqlAdminAuditRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public async ValueTask AppendAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ano_admin_action_audit (
                    audit_id, action_id, actor_steam_id, target_steam_id, reason, occurred_at_utc)
                VALUES (
                    @auditId, @actionId, @actorSteamId, @targetSteamId, @reason, @occurredAtUtc)
                """;
            AddParameter(command, "@auditId", entry.Id.ToString("D"));
            AddParameter(command, "@actionId", entry.Action.Value);
            AddParameter(command, "@actorSteamId", entry.ActorId is null ? DBNull.Value : entry.ActorId.SteamId64);
            AddParameter(command, "@targetSteamId", entry.TargetId is null ? DBNull.Value : entry.TargetId.SteamId64);
            AddParameter(command, "@reason", entry.Reason);
            AddParameter(command, "@occurredAtUtc", entry.OccurredAtUtc.UtcDateTime);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<AdminAuditEntry>> GetRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
        => QueryAsync(null, AdminAuditValidation.ValidateLimit(limit), cancellationToken);

    public ValueTask<IReadOnlyList<AdminAuditEntry>> GetTargetHistoryAsync(
        PlayerId targetId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        return QueryAsync(targetId, AdminAuditValidation.ValidateLimit(limit), cancellationToken);
    }

    private ValueTask<IReadOnlyList<AdminAuditEntry>> QueryAsync(
        PlayerId? targetId,
        int limit,
        CancellationToken cancellationToken)
        => _database.WithConnectionAsync<IReadOnlyList<AdminAuditEntry>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT audit_id, action_id, actor_steam_id, target_steam_id, reason, occurred_at_utc
                FROM ano_admin_action_audit
                """ + (targetId is null ? "" : " WHERE target_steam_id = @targetSteamId") + """
                
                ORDER BY occurred_at_utc DESC, audit_id DESC
                LIMIT @limit
                """;
            if (targetId is not null)
            {
                AddParameter(command, "@targetSteamId", targetId.SteamId64);
            }

            AddParameter(command, "@limit", limit);
            var result = new List<AdminAuditEntry>();
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                result.Add(new AdminAuditEntry(
                    ReadGuid(reader.GetValue(0)),
                    new AdminActionId(reader.GetString(1)),
                    ReadPlayer(reader, 2),
                    ReadPlayer(reader, 3),
                    reader.GetString(4),
                    new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc))));
            }

            return result;
        }, cancellationToken);

    private static Guid ReadGuid(object value)
        => value is Guid guid ? guid : Guid.Parse(
            Convert.ToString(value, CultureInfo.InvariantCulture)
            ?? throw new InvalidDataException("An admin audit id could not be read."));

    private static PlayerId? ReadPlayer(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : new PlayerId(Convert.ToUInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture));

    private static void AddParameter(DbCommand command, string name, object value)
        => MigrationRunner.AddParameter(command, name, value);
}
