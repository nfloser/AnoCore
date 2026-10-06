using System.Data;
using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class MySqlProgressionGrantRepository : IProgressionGrantRepository
{
    private readonly IDatabase _database;

    public MySqlProgressionGrantRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public ValueTask<ProgressionLifetimeState> ReadLifetimeAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        return _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT lifetime_xp, revision
                FROM ano_progression_accounts
                WHERE player_steam_id = @player
                """;
            Add(command, "@player", playerId.SteamId64);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                return new ProgressionLifetimeState(playerId, 0, 0);

            var lifetimeXp = ReadNonnegativeInt64(reader.GetValue(0), "lifetime_xp");
            var revision = ReadNonnegativeInt64(reader.GetValue(1), "revision");
            return new ProgressionLifetimeState(playerId, lifetimeXp, revision);
        }, cancellationToken);
    }

    public ValueTask<ProgressionGrantRecord?> ReadGrantAsync(
        PlayerId playerId,
        string grantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ValidateGrantId(grantId);
        return _database.WithConnectionAsync(
            (connection, token) => ReadGrantAsync(
                connection, transaction: null, playerId, grantId, forUpdate: false, token),
            cancellationToken);
    }

    public ValueTask<ProgressionGrantCommitResult> ApplyAsync(
        PlayerId playerId,
        ProgressionGrantCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(candidate);
        candidate = NormalizeAndValidate(candidate);

        return _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await EnsureAccountAsync(
                connection, transaction, playerId, token).ConfigureAwait(false);
            var state = await ReadLifetimeForUpdateAsync(
                connection, transaction, playerId, token).ConfigureAwait(false);

            var existing = await ReadGrantAsync(
                connection, transaction, playerId, candidate.GrantId, forUpdate: false, token)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                if (!MatchesCandidate(existing, candidate))
                    throw new ProgressionGrantConflictException(playerId, candidate.GrantId);
                return new ProgressionGrantCommitResult(false, existing);
            }

            var lifetimeAfter = checked(state.LifetimeXp + candidate.AwardedXp);
            var revisionAfter = checked(state.Revision + 1);
            var grant = new ProgressionGrantRecord(
                playerId,
                candidate.GrantId,
                candidate.Source,
                candidate.BaseXp,
                candidate.AwardedXp,
                candidate.Reason,
                candidate.OccurredAtUtc,
                candidate.BoostId,
                candidate.BoostMultiplier,
                lifetimeAfter,
                revisionAfter);

            await InsertGrantAsync(
                connection, transaction, grant, token).ConfigureAwait(false);
            await UpdateLifetimeAsync(
                connection, transaction, playerId, state.Revision,
                lifetimeAfter, revisionAfter, token).ConfigureAwait(false);

            return new ProgressionGrantCommitResult(true, grant);
        }, IsolationLevel.ReadCommitted, cancellationToken);
    }

    private static async ValueTask EnsureAccountAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId playerId,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_progression_accounts (
                player_steam_id, lifetime_xp, revision, updated_at_utc)
            VALUES (@player, 0, 0, @updated)
            ON DUPLICATE KEY UPDATE player_steam_id = player_steam_id
            """;
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@updated", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async ValueTask<ProgressionLifetimeState> ReadLifetimeForUpdateAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId playerId,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT lifetime_xp, revision
            FROM ano_progression_accounts
            WHERE player_steam_id = @player
            FOR UPDATE
            """;
        Add(command, "@player", playerId.SteamId64);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidOperationException("Progression account row disappeared during grant.");

        return new ProgressionLifetimeState(
            playerId,
            ReadNonnegativeInt64(reader.GetValue(0), "lifetime_xp"),
            ReadNonnegativeInt64(reader.GetValue(1), "revision"));
    }

    private static async ValueTask<ProgressionGrantRecord?> ReadGrantAsync(
        DbConnection connection,
        DbTransaction? transaction,
        PlayerId playerId,
        string grantId,
        bool forUpdate,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT source, base_xp, awarded_xp, reason, occurred_at_utc,
                boost_id, boost_multiplier, lifetime_xp_after,
                account_revision_after
            FROM ano_progression_grants
            WHERE player_steam_id = @player AND grant_id = @grant
            """ + (forUpdate ? " FOR UPDATE" : string.Empty);
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@grant", grantId);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            return null;

        var sourceValue = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
        if (!Enum.IsDefined(typeof(ProgressionXpSource), sourceValue))
            throw new InvalidOperationException("Stored progression grant has an invalid source.");

        return new ProgressionGrantRecord(
            playerId,
            grantId,
            (ProgressionXpSource)sourceValue,
            ReadNonnegativeInt64(reader.GetValue(1), "base_xp"),
            ReadNonnegativeInt64(reader.GetValue(2), "awarded_xp"),
            reader.GetString(3),
            Utc(reader.GetDateTime(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetDecimal(6),
            ReadNonnegativeInt64(reader.GetValue(7), "lifetime_xp_after"),
            ReadNonnegativeInt64(reader.GetValue(8), "account_revision_after"));
    }

    private static async ValueTask InsertGrantAsync(
        DbConnection connection,
        DbTransaction transaction,
        ProgressionGrantRecord grant,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_progression_grants (
                player_steam_id, grant_id, source, base_xp, awarded_xp,
                reason, occurred_at_utc, boost_id, boost_multiplier,
                lifetime_xp_after, account_revision_after, created_at_utc)
            VALUES (
                @player, @grant, @source, @base, @awarded,
                @reason, @occurred, @boost, @multiplier,
                @lifetimeAfter, @revisionAfter, @created)
            """;
        Add(command, "@player", grant.PlayerId.SteamId64);
        Add(command, "@grant", grant.GrantId);
        Add(command, "@source", (int)grant.Source);
        Add(command, "@base", grant.BaseXp);
        Add(command, "@awarded", grant.AwardedXp);
        Add(command, "@reason", grant.Reason);
        Add(command, "@occurred", grant.OccurredAtUtc.UtcDateTime);
        Add(command, "@boost", grant.BoostId is null ? DBNull.Value : grant.BoostId);
        Add(command, "@multiplier", grant.BoostMultiplier);
        Add(command, "@lifetimeAfter", grant.LifetimeXpAfter);
        Add(command, "@revisionAfter", grant.AccountRevisionAfter);
        Add(command, "@created", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async ValueTask UpdateLifetimeAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId playerId,
        long expectedRevision,
        long lifetimeAfter,
        long revisionAfter,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ano_progression_accounts
            SET lifetime_xp = @lifetime,
                revision = @revision,
                updated_at_utc = @updated
            WHERE player_steam_id = @player AND revision = @expected
            """;
        Add(command, "@lifetime", lifetimeAfter);
        Add(command, "@revision", revisionAfter);
        Add(command, "@updated", DateTime.UtcNow);
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@expected", expectedRevision);
        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Progression account revision changed unexpectedly.");
    }

    private static ProgressionGrantCandidate NormalizeAndValidate(
        ProgressionGrantCandidate candidate)
    {
        ValidateGrantId(candidate.GrantId);
        if (!PrintableBounded(candidate.Reason, ProgressionGrantService.MaxReasonLength)
            || candidate.BaseXp < 0
            || candidate.AwardedXp < 0
            || !Enum.IsDefined(candidate.Source)
            || candidate.BoostMultiplier is < 1m or > ProgressionDefinitionSnapshot.MaxBoostMultiplier
            || candidate.BoostId is not null
                && !PrintableBounded(candidate.BoostId, 64)
            || candidate.BoostId is null && candidate.BoostMultiplier != 1m
            || candidate.Source == ProgressionXpSource.Administration
                && (candidate.BoostId is not null || candidate.BoostMultiplier != 1m))
        {
            throw new ArgumentException("Progression grant candidate is invalid.", nameof(candidate));
        }

        var expectedAward = checked((long)decimal.Truncate(
            candidate.BaseXp * candidate.BoostMultiplier));
        if (expectedAward != candidate.AwardedXp)
            throw new ArgumentException(
                "Awarded XP does not match the base XP and multiplier.",
                nameof(candidate));

        return candidate with
        {
            OccurredAtUtc = NormalizeUtc(candidate.OccurredAtUtc),
        };
    }

    private static void ValidateGrantId(string? grantId)
    {
        if (!PrintableBounded(grantId, ProgressionGrantService.MaxGrantIdLength)
            || grantId!.Any(character => character > 0x7f))
        {
            throw new ArgumentException(
                "Progression grant ID must be printable ASCII without surrounding whitespace.",
                nameof(grantId));
        }
    }


    private static DateTimeOffset NormalizeUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.UtcDateTime.Ticks;
        ticks -= ticks % TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
    }

    private static bool PrintableBounded(string? value, int maximum)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximum
            && value == value.Trim()
            && value.All(character => !char.IsControl(character));

    private static bool MatchesCandidate(
        ProgressionGrantRecord existing,
        ProgressionGrantCandidate candidate)
        => existing.Source == candidate.Source
            && existing.BaseXp == candidate.BaseXp
            && existing.AwardedXp == candidate.AwardedXp
            && string.Equals(existing.Reason, candidate.Reason, StringComparison.Ordinal)
            && existing.OccurredAtUtc == candidate.OccurredAtUtc
            && string.Equals(existing.BoostId, candidate.BoostId, StringComparison.Ordinal)
            && existing.BoostMultiplier == candidate.BoostMultiplier;

    private static DateTimeOffset Utc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static long ReadNonnegativeInt64(object value, string field)
    {
        try
        {
            var converted = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (converted < 0)
                throw new InvalidOperationException(
                    $"Stored progression field '{field}' cannot be negative.");
            return converted;
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException(
                $"Stored progression field '{field}' exceeds supported XP range.",
                exception);
        }
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
