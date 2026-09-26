using System.Data.Common;
using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Stats;

public sealed class MySqlRankAdjustmentAdministrationService
    : IRankAdjustmentAdministrationService
{
    private readonly IDatabase _database;
    private readonly Func<Guid> _newAuditId;

    public MySqlRankAdjustmentAdministrationService(
        IDatabase database,
        Func<Guid>? newAuditId = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _newAuditId = newAuditId ?? Guid.NewGuid;
    }

    public ValueTask<RankAdjustmentAdminResult> ApplyAsync(
        RankAdjustmentAdminOperation operation,
        PlayerId targetId,
        long points,
        PlayerId? actorId,
        string reason,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        ValidatePoints(operation, points);
        var action = new AdminActionId($"rank.adjustment.{operation.ToString().ToLowerInvariant()}");
        var auditReason = AdminAuditValidation.NormalizeReason(
            operation == RankAdjustmentAdminOperation.Reset
                ? reason
                : $"points={points}; {reason}");
        var auditId = _newAuditId();
        if (auditId == Guid.Empty)
            throw new InvalidOperationException("The audit id generator returned an empty id.");
        var occurred = occurredAtUtc.ToUniversalTime();

        return _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            var previous = await LockAsync(connection, transaction, targetId, token)
                .ConfigureAwait(false);
            var current = Calculate(operation, previous, points);

            if (operation == RankAdjustmentAdminOperation.Reset)
                await DeleteAsync(connection, transaction, targetId, token).ConfigureAwait(false);
            else
                await UpsertAsync(connection, transaction, targetId, current, actorId, occurred, token)
                    .ConfigureAwait(false);

            await AppendAuditAsync(connection, transaction, auditId, action, actorId,
                targetId, auditReason, occurred, token).ConfigureAwait(false);
            return new RankAdjustmentAdminResult(previous, current, auditId);
        }, cancellationToken: cancellationToken);
    }

    private static void ValidatePoints(RankAdjustmentAdminOperation operation, long points)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        if (operation is RankAdjustmentAdminOperation.Give or RankAdjustmentAdminOperation.Take
            && points <= 0)
            throw new ArgumentOutOfRangeException(nameof(points), "Give/take points must be positive.");
        if (operation == RankAdjustmentAdminOperation.Reset && points != 0)
            throw new ArgumentOutOfRangeException(nameof(points), "Reset does not accept points.");
        if (points is < -MySqlRankAdjustmentRepository.MaximumAbsolutePoints
            or > MySqlRankAdjustmentRepository.MaximumAbsolutePoints)
            throw new ArgumentOutOfRangeException(nameof(points));
    }

    private static long Calculate(RankAdjustmentAdminOperation operation, long previous, long points)
    {
        long current;
        try
        {
            current = operation switch
            {
                RankAdjustmentAdminOperation.Give => checked(previous + points),
                RankAdjustmentAdminOperation.Take => checked(previous - points),
                RankAdjustmentAdminOperation.Set => points,
                RankAdjustmentAdminOperation.Reset => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            };
        }
        catch (OverflowException exception)
        {
            throw new ArgumentOutOfRangeException(nameof(points),
                "The resulting rank adjustment is outside the supported range.", exception);
        }

        if (current is < -MySqlRankAdjustmentRepository.MaximumAbsolutePoints
            or > MySqlRankAdjustmentRepository.MaximumAbsolutePoints)
            throw new ArgumentOutOfRangeException(nameof(points),
                "The resulting rank adjustment is outside the supported range.");
        return current;
    }

    private static async ValueTask<long> LockAsync(DbConnection connection,
        DbTransaction transaction, PlayerId targetId, CancellationToken cancellationToken)
    {
        await using var seed = connection.CreateCommand();
        seed.Transaction = transaction;
        seed.CommandText = """
            INSERT INTO ano_rank_adjustments (
                player_steam_id, points, updated_by_steam_id, updated_at_utc)
            VALUES (@target, 0, NULL, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE player_steam_id = player_steam_id
            """;
        Add(seed, "@target", targetId.SteamId64);
        await seed.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT points FROM ano_rank_adjustments
            WHERE player_steam_id = @target FOR UPDATE
            """;
        Add(command, "@target", targetId.SteamId64);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async ValueTask UpsertAsync(DbConnection connection,
        DbTransaction transaction, PlayerId targetId, long points, PlayerId? actorId,
        DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ano_rank_adjustments SET points = @points,
                updated_by_steam_id = @actor, updated_at_utc = @occurred
            WHERE player_steam_id = @target
            """;
        Add(command, "@points", points);
        Add(command, "@actor", actorId is null ? DBNull.Value : actorId.SteamId64);
        Add(command, "@occurred", occurredAtUtc.UtcDateTime);
        Add(command, "@target", targetId.SteamId64);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask DeleteAsync(DbConnection connection,
        DbTransaction transaction, PlayerId targetId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM ano_rank_adjustments WHERE player_steam_id = @target";
        Add(command, "@target", targetId.SteamId64);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask AppendAuditAsync(DbConnection connection,
        DbTransaction transaction, Guid auditId, AdminActionId action, PlayerId? actorId,
        PlayerId targetId, string reason, DateTimeOffset occurredAtUtc,
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
        Add(command, "@action", action.Value);
        Add(command, "@actor", actorId is null ? DBNull.Value : actorId.SteamId64);
        Add(command, "@target", targetId.SteamId64);
        Add(command, "@reason", reason);
        Add(command, "@occurred", occurredAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Add(DbCommand command, string name, object value)
        => MigrationRunner.AddParameter(command, name, value);
}
