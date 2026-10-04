using System.Data.Common;
using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Stats;

public sealed class MySqlStatisticsResetAdministrationService
    : IStatisticsResetAdministrationService
{
    private readonly IDatabase _database;
    private readonly Func<Guid> _newAuditId;

    public MySqlStatisticsResetAdministrationService(
        IDatabase database,
        Func<Guid>? newAuditId = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _newAuditId = newAuditId ?? Guid.NewGuid;
    }

    public ValueTask<StatisticsResetResult> ResetAsync(
        PlayerId targetId,
        PlayerId? actorId,
        string reason,
        DateTimeOffset resetAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        var auditReason = AdminAuditValidation.NormalizeReason(reason);
        var cutoff = NormalizeTimestamp(resetAtUtc);
        var auditId = _newAuditId();
        if (auditId == Guid.Empty)
            throw new InvalidOperationException("The audit id generator returned an empty id.");

        return _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await SeedCutoffAsync(connection, transaction, targetId, token)
                .ConfigureAwait(false);
            var previous = await LockCurrentAsync(
                connection, transaction, targetId, token).ConfigureAwait(false);
            if (previous is not null && cutoff <= previous.Value)
                throw new ArgumentOutOfRangeException(nameof(resetAtUtc),
                    "A statistics reset must advance the existing cutoff.");

            await UpsertCutoffAsync(
                connection, transaction, targetId, actorId, cutoff, token).ConfigureAwait(false);
            await AppendAuditAsync(
                connection, transaction, auditId, actorId, targetId,
                auditReason, cutoff, token).ConfigureAwait(false);
            return new StatisticsResetResult(previous, cutoff, auditId);
        }, cancellationToken: cancellationToken);
    }

    private static async ValueTask SeedCutoffAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId targetId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_statistics_resets (
                player_steam_id, reset_at_utc, updated_by_steam_id)
            VALUES (@target, '1970-01-01 00:00:00.000000', NULL)
            ON DUPLICATE KEY UPDATE player_steam_id = player_steam_id
            """;
        Add(command, "@target", targetId.SteamId64);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<DateTimeOffset?> LockCurrentAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId targetId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT reset_at_utc
            FROM ano_statistics_resets
            WHERE player_steam_id = @target
            FOR UPDATE
            """;
        Add(command, "@target", targetId.SteamId64);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null || value is DBNull) return null;
        var cutoff = new DateTimeOffset(
            DateTime.SpecifyKind(Convert.ToDateTime(value), DateTimeKind.Utc));
        return cutoff == DateTimeOffset.UnixEpoch ? null : cutoff;
    }

    private static async ValueTask UpsertCutoffAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId targetId,
        PlayerId? actorId,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_statistics_resets (
                player_steam_id, reset_at_utc, updated_by_steam_id)
            VALUES (@target, @cutoff, @actor)
            ON DUPLICATE KEY UPDATE
                reset_at_utc = VALUES(reset_at_utc),
                updated_by_steam_id = VALUES(updated_by_steam_id)
            """;
        Add(command, "@target", targetId.SteamId64);
        Add(command, "@cutoff", cutoff.UtcDateTime);
        Add(command, "@actor", actorId is null ? DBNull.Value : actorId.SteamId64);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask AppendAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid auditId,
        PlayerId? actorId,
        PlayerId targetId,
        string reason,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_admin_action_audit (
                audit_id, action_id, actor_steam_id, target_steam_id, reason, occurred_at_utc)
            VALUES (@id, @action, @actor, @target, @reason, @occurred)
            """;
        Add(command, "@id", auditId.ToString("D"));
        Add(command, "@action", "statistics.reset");
        Add(command, "@actor", actorId is null ? DBNull.Value : actorId.SteamId64);
        Add(command, "@target", targetId.SteamId64);
        Add(command, "@reason", reason);
        Add(command, "@occurred", occurredAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }

    private static void Add(DbCommand command, string name, object value)
        => MigrationRunner.AddParameter(command, name, value);
}
