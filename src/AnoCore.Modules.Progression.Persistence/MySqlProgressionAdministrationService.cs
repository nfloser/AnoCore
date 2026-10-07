using System.Data;
using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class MySqlProgressionAdministrationService(IDatabase database) : IProgressionAdministrationService
{
    private readonly IDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public static async ValueTask EnsureReadyAsync(IDatabase database, CancellationToken cancellationToken = default)
    {
        await ProgressionPersistenceBootstrap.EnsureReadyAsync(database, cancellationToken).ConfigureAwait(false);
        await new MigrationRunner(database, [new AdminAuditSchemaMigration003(), new ProgressionAdministrationSchemaMigration019()])
            .ApplyPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<ProgressionAdminResult> ApplyAsync(ProgressionAdminRequest request, CancellationToken cancellationToken = default)
    {
        request = ProgressionAdministrationPolicy.Validate(request);
        return _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await MySqlProgressionGrantRepository.EnsureAccountAsync(connection, transaction, request.Target, token).ConfigureAwait(false);
            var state = await MySqlProgressionGrantRepository.ReadLifetimeForUpdateAsync(connection, transaction, request.Target, token).ConfigureAwait(false);
            var existing = await ReadRequestAsync(connection, transaction, request, token).ConfigureAwait(false);
            if (existing is not null) return existing;
            var current = ProgressionAdministrationPolicy.Calculate(request.Operation, state.LifetimeXp, request.Amount);
            var revision = checked(state.Revision + 1);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ano_progression_admin_requests
                    (request_id, operation, target_steam_id, amount, actor_steam_id, reason, occurred_at_utc, previous_xp, current_xp, revision)
                VALUES (@id, @operation, @target, @amount, @actor, @reason, @at, @previous, @current, @revision)
                """;
            Bind(command, request, state.LifetimeXp, current, revision);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            command.CommandText = """
                UPDATE ano_progression_accounts SET lifetime_xp = @current, revision = @revision, updated_at_utc = @at
                WHERE player_steam_id = @target AND revision = @expected
                """;
            Add(command, "@expected", state.Revision);
            if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("Progression revision changed during administration.");
            command.CommandText = """
                INSERT INTO ano_admin_action_audit (audit_id, action_id, actor_steam_id, target_steam_id, reason, occurred_at_utc)
                VALUES (@id, @action, @actor, @target, @auditReason, @at)
                """;
            Add(command, "@action", "progression.xp." + request.Operation.ToString().ToLowerInvariant());
            Add(command, "@auditReason", string.Create(CultureInfo.InvariantCulture,
                $"amount={request.Amount}; xp={state.LifetimeXp}->{current}; {request.Reason}"));
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return new ProgressionAdminResult(true, request.RequestId, state.LifetimeXp, current, revision);
        }, IsolationLevel.ReadCommitted, cancellationToken);
    }

    private static async ValueTask<ProgressionAdminResult?> ReadRequestAsync(DbConnection connection,
        DbTransaction transaction, ProgressionAdminRequest request, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT operation, target_steam_id, amount, actor_steam_id, reason, occurred_at_utc, previous_xp, current_xp, revision
            FROM ano_progression_admin_requests WHERE request_id = @id
            """;
        Add(command, "@id", request.RequestId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        var actor = reader.IsDBNull(3) ? (ulong?)null : Convert.ToUInt64(reader.GetValue(3), CultureInfo.InvariantCulture);
        var at = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc));
        if (reader.GetByte(0) != (byte)request.Operation || Convert.ToUInt64(reader.GetValue(1), CultureInfo.InvariantCulture) != request.Target.SteamId64
            || reader.GetInt64(2) != request.Amount || actor != request.Actor?.SteamId64
            || reader.GetString(4) != request.Reason || at != request.OccurredAtUtc)
            throw new ArgumentException("XP administration request ID conflicts with an existing request.");
        return new(false, request.RequestId, reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8));
    }

    private static void Bind(DbCommand command, ProgressionAdminRequest request, long previous, long current, long revision)
    {
        Add(command, "@id", request.RequestId.ToString("D"));
        Add(command, "@operation", (byte)request.Operation);
        Add(command, "@target", request.Target.SteamId64);
        Add(command, "@amount", request.Amount);
        Add(command, "@actor", request.Actor is null ? DBNull.Value : request.Actor.SteamId64);
        Add(command, "@reason", request.Reason);
        Add(command, "@at", request.OccurredAtUtc.UtcDateTime);
        Add(command, "@previous", previous);
        Add(command, "@current", current);
        Add(command, "@revision", revision);
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
