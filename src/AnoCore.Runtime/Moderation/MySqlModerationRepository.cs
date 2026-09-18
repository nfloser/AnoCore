using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Moderation;

public sealed class MySqlModerationRepository : IModerationRepository
{
    private const string SanctionColumns = """
        sanction_id,
        target_steam_id,
        actor_steam_id,
        restriction,
        reason,
        created_at_utc,
        expires_at_utc,
        revoked_at_utc,
        revoked_by_steam_id,
        revocation_reason
        """;

    private readonly IDatabase _database;

    public MySqlModerationRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public async ValueTask AddAsync(
        IReadOnlyCollection<ModerationSanction> sanctions,
        ModerationAuditEntry audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sanctions);
        ArgumentNullException.ThrowIfNull(audit);
        if (sanctions.Count == 0)
        {
            throw new ArgumentException("At least one moderation sanction is required.", nameof(sanctions));
        }

        ValidateAppliedBatch(sanctions, audit);
        await _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            foreach (var sanction in sanctions)
            {
                await InsertSanctionAsync(connection, transaction, sanction, token).ConfigureAwait(false);
            }

            await InsertAuditAsync(connection, transaction, audit, token).ConfigureAwait(false);
            return true;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<ModerationSanction>> GetActiveAsync(
        PlayerId targetId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        var instant = atUtc.ToUniversalTime();
        return _database.WithConnectionAsync<IReadOnlyList<ModerationSanction>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {SanctionColumns}
                FROM ano_moderation_sanctions
                WHERE target_steam_id = @targetSteamId
                  AND created_at_utc <= @atUtc
                  AND (revoked_at_utc IS NULL OR revoked_at_utc > @atUtc)
                  AND (expires_at_utc IS NULL OR expires_at_utc > @atUtc)
                ORDER BY created_at_utc, sanction_id
                """;
            AddParameter(command, "@targetSteamId", targetId.SteamId64);
            AddParameter(command, "@atUtc", instant.UtcDateTime);
            return await ReadSanctionsAsync(command, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public ValueTask<IReadOnlyList<ModerationSanction>> GetHistoryAsync(
        PlayerId targetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        return _database.WithConnectionAsync<IReadOnlyList<ModerationSanction>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {SanctionColumns}
                FROM ano_moderation_sanctions
                WHERE target_steam_id = @targetSteamId
                ORDER BY created_at_utc, sanction_id
                """;
            AddParameter(command, "@targetSteamId", targetId.SteamId64);
            return await ReadSanctionsAsync(command, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ModerationSanction>> RevokeActiveAsync(
        PlayerId targetId,
        ModerationRestriction restrictions,
        PlayerId? actorId,
        string reason,
        DateTimeOffset atUtc,
        ModerationAuditEntry audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        ArgumentNullException.ThrowIfNull(audit);
        ModerationValidation.ValidateRestrictions(restrictions);
        var normalizedReason = ModerationValidation.NormalizeReason(reason);

        if (audit.Action != ModerationAuditAction.Revoked
            || audit.TargetId != targetId
            || audit.Restrictions != restrictions)
        {
            throw new ArgumentException("The moderation audit does not match the revocation operation.", nameof(audit));
        }

        var revokedAt = atUtc.ToUniversalTime();
        return await _database.InTransactionAsync<IReadOnlyList<ModerationSanction>>(
            async (connection, transaction, token) =>
            {
                var active = await ReadActiveForUpdateAsync(
                    connection,
                    transaction,
                    targetId,
                    revokedAt,
                    token).ConfigureAwait(false);
                var matching = active
                    .Where(value => restrictions.HasFlag(value.Restriction))
                    .ToArray();
                if (matching.Length == 0)
                {
                    return [];
                }

                var revoked = new List<ModerationSanction>(matching.Length);
                foreach (var sanction in matching)
                {
                    await using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = """
                        UPDATE ano_moderation_sanctions
                        SET revoked_at_utc = @revokedAtUtc,
                            revoked_by_steam_id = @revokedBySteamId,
                            revocation_reason = @revocationReason
                        WHERE sanction_id = @sanctionId
                          AND revoked_at_utc IS NULL
                        """;
                    AddParameter(update, "@revokedAtUtc", revokedAt.UtcDateTime);
                    AddNullablePlayer(update, "@revokedBySteamId", actorId);
                    AddParameter(update, "@revocationReason", normalizedReason);
                    AddParameter(update, "@sanctionId", sanction.Id.ToString("D"));
                    var affected = await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    if (affected != 1)
                    {
                        throw new InvalidOperationException(
                            $"Moderation sanction '{sanction.Id:D}' changed while it was locked for revocation.");
                    }

                    revoked.Add(sanction.Revoke(actorId, normalizedReason, revokedAt));
                }

                await InsertAuditAsync(connection, transaction, audit, token).ConfigureAwait(false);
                return revoked;
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<ModerationAuditEntry>> GetAuditHistoryAsync(
        PlayerId targetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        return _database.WithConnectionAsync<IReadOnlyList<ModerationAuditEntry>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    audit_id,
                    target_steam_id,
                    actor_steam_id,
                    action,
                    restrictions,
                    reason,
                    occurred_at_utc
                FROM ano_moderation_audit
                WHERE target_steam_id = @targetSteamId
                ORDER BY occurred_at_utc, audit_id
                """;
            AddParameter(command, "@targetSteamId", targetId.SteamId64);

            var result = new List<ModerationAuditEntry>();
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                result.Add(new ModerationAuditEntry(
                    ReadGuid(reader, 0),
                    ReadPlayer(reader, 1)!,
                    ReadPlayer(reader, 2),
                    (ModerationAuditAction)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
                    (ModerationRestriction)Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture),
                    reader.GetString(5),
                    ToUtcOffset(reader.GetDateTime(6))));
            }

            return result;
        }, cancellationToken);
    }

    private static void ValidateAppliedBatch(
        IReadOnlyCollection<ModerationSanction> sanctions,
        ModerationAuditEntry audit)
    {
        if (audit.Action != ModerationAuditAction.Applied)
        {
            throw new ArgumentException("Applying sanctions requires an applied audit entry.", nameof(audit));
        }

        if (sanctions.Any(value => value.TargetId != audit.TargetId))
        {
            throw new ArgumentException("Every sanction must target the same player as the audit entry.", nameof(sanctions));
        }

        var restrictions = sanctions.Aggregate(
            ModerationRestriction.None,
            (current, value) => current | value.Restriction);
        if (restrictions != audit.Restrictions)
        {
            throw new ArgumentException("The applied sanctions do not match the audit restrictions.", nameof(sanctions));
        }
    }

    private static async ValueTask InsertSanctionAsync(
        DbConnection connection,
        DbTransaction transaction,
        ModerationSanction sanction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_moderation_sanctions (
                sanction_id,
                target_steam_id,
                actor_steam_id,
                restriction,
                reason,
                created_at_utc,
                expires_at_utc,
                revoked_at_utc,
                revoked_by_steam_id,
                revocation_reason)
            VALUES (
                @sanctionId,
                @targetSteamId,
                @actorSteamId,
                @restriction,
                @reason,
                @createdAtUtc,
                @expiresAtUtc,
                @revokedAtUtc,
                @revokedBySteamId,
                @revocationReason)
            """;
        AddParameter(command, "@sanctionId", sanction.Id.ToString("D"));
        AddParameter(command, "@targetSteamId", sanction.TargetId.SteamId64);
        AddNullablePlayer(command, "@actorSteamId", sanction.ActorId);
        AddParameter(command, "@restriction", (int)sanction.Restriction);
        AddParameter(command, "@reason", sanction.Reason);
        AddParameter(command, "@createdAtUtc", sanction.CreatedAtUtc.UtcDateTime);
        AddNullableDateTime(command, "@expiresAtUtc", sanction.ExpiresAtUtc);
        AddNullableDateTime(command, "@revokedAtUtc", sanction.RevokedAtUtc);
        AddNullablePlayer(command, "@revokedBySteamId", sanction.RevokedById);
        AddNullableString(command, "@revocationReason", sanction.RevocationReason);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask InsertAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        ModerationAuditEntry audit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_moderation_audit (
                audit_id,
                target_steam_id,
                actor_steam_id,
                action,
                restrictions,
                reason,
                occurred_at_utc)
            VALUES (
                @auditId,
                @targetSteamId,
                @actorSteamId,
                @action,
                @restrictions,
                @reason,
                @occurredAtUtc)
            """;
        AddParameter(command, "@auditId", audit.Id.ToString("D"));
        AddParameter(command, "@targetSteamId", audit.TargetId.SteamId64);
        AddNullablePlayer(command, "@actorSteamId", audit.ActorId);
        AddParameter(command, "@action", (int)audit.Action);
        AddParameter(command, "@restrictions", (int)audit.Restrictions);
        AddParameter(command, "@reason", audit.Reason);
        AddParameter(command, "@occurredAtUtc", audit.OccurredAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<IReadOnlyList<ModerationSanction>> ReadActiveForUpdateAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId targetId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {SanctionColumns}
            FROM ano_moderation_sanctions
            WHERE target_steam_id = @targetSteamId
              AND created_at_utc <= @atUtc
              AND revoked_at_utc IS NULL
              AND (expires_at_utc IS NULL OR expires_at_utc > @atUtc)
            ORDER BY created_at_utc, sanction_id
            FOR UPDATE
            """;
        AddParameter(command, "@targetSteamId", targetId.SteamId64);
        AddParameter(command, "@atUtc", atUtc.UtcDateTime);
        return await ReadSanctionsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<IReadOnlyList<ModerationSanction>> ReadSanctionsAsync(
        DbCommand command,
        CancellationToken cancellationToken)
    {
        var result = new List<ModerationSanction>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new ModerationSanction(
                ReadGuid(reader, 0),
                ReadPlayer(reader, 1)!,
                ReadPlayer(reader, 2),
                (ModerationRestriction)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
                reader.GetString(4),
                ToUtcOffset(reader.GetDateTime(5)),
                ReadDateTime(reader, 6),
                ReadDateTime(reader, 7),
                ReadPlayer(reader, 8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return result;
    }

    private static Guid ReadGuid(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            Guid guid => guid,
            string text => Guid.Parse(text),
            _ => Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)
                ?? throw new InvalidDataException("A moderation id could not be read.")),
        };
    }

    private static PlayerId? ReadPlayer(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : new PlayerId(Convert.ToUInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture));

    private static DateTimeOffset? ReadDateTime(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : ToUtcOffset(reader.GetDateTime(ordinal));

    private static DateTimeOffset ToUtcOffset(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static void AddNullablePlayer(DbCommand command, string name, PlayerId? playerId)
        => AddParameter(command, name, playerId is null ? DBNull.Value : playerId.SteamId64);

    private static void AddNullableDateTime(DbCommand command, string name, DateTimeOffset? value)
        => AddParameter(command, name, value is null ? DBNull.Value : value.Value.UtcDateTime);

    private static void AddNullableString(DbCommand command, string name, string? value)
        => AddParameter(command, name, value is null ? DBNull.Value : value);

    private static void AddParameter(DbCommand command, string name, object value)
        => MigrationRunner.AddParameter(command, name, value);
}
