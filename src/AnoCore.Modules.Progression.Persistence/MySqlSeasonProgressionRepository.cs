using System.Data;
using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class MySqlSeasonProgressionRepository : ISeasonProgressionRepository
{
    private readonly IDatabase _database;

    public MySqlSeasonProgressionRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public ValueTask<SeasonProgressionState> ReadAsync(
        PlayerId playerId,
        string seasonId,
        int seasonVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ValidateSeasonKey(seasonId, seasonVersion);
        return _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT season_version, season_xp, revision
                FROM ano_progression_season_accounts
                WHERE player_steam_id = @player AND season_id = @season
                """;
            Add(command, "@player", playerId.SteamId64);
            Add(command, "@season", seasonId);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                return new SeasonProgressionState(playerId, seasonId, seasonVersion, 0, 0);

            var storedVersion = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            if (storedVersion != seasonVersion)
                throw new InvalidOperationException("Stored season progression version does not match the requested definition.");

            return new SeasonProgressionState(
                playerId,
                seasonId,
                storedVersion,
                ReadNonnegativeInt64(reader.GetValue(1), "season_xp"),
                ReadNonnegativeInt64(reader.GetValue(2), "revision"));
        }, cancellationToken);
    }

    public ValueTask<SeasonXpGrantRecord?> ReadGrantAsync(
        PlayerId playerId,
        string seasonId,
        string grantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ValidateSeasonId(seasonId);
        ValidateGrantId(grantId);
        return _database.WithConnectionAsync(
            (connection, token) => ReadGrantAsync(
                connection, transaction: null, playerId, seasonId, grantId, token),
            cancellationToken);
    }

    public ValueTask<SeasonXpGrantCommitResult> ApplyAsync(
        PlayerId playerId,
        SeasonXpGrantCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(candidate);
        candidate = NormalizeAndValidate(candidate);

        return _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            var season = await LockSeasonAsync(
                connection, transaction, candidate.SeasonId, candidate.SeasonVersion, token)
                .ConfigureAwait(false);

            if (candidate.OccurredAtUtc < season.StartsAtUtc
                || candidate.OccurredAtUtc >= season.EndsAtUtc)
            {
                throw new SeasonNotActiveException(candidate.OccurredAtUtc);
            }

            await EnsureAccountAsync(
                connection, transaction, playerId, candidate, token).ConfigureAwait(false);
            var state = await ReadForUpdateAsync(
                connection, transaction, playerId, candidate, token).ConfigureAwait(false);

            var existing = await ReadGrantAsync(
                connection, transaction, playerId, candidate.SeasonId, candidate.GrantId, token)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                if (!MatchesOriginalCandidate(existing, candidate))
                    throw new SeasonXpGrantConflictException(
                        playerId, candidate.SeasonId, candidate.GrantId);
                return new SeasonXpGrantCommitResult(false, existing);
            }

            if (season.ClosedAtUtc is not null)
                throw new SeasonProgressionClosedException(
                    candidate.SeasonId, candidate.SeasonVersion);

            var seasonXpAfter = checked(state.SeasonXp + candidate.AwardedXp);
            if (seasonXpAfter < 0)
                throw new InvalidOperationException("Season XP cannot be adjusted below zero.");

            var revisionAfter = checked(state.Revision + 1);
            var grant = new SeasonXpGrantRecord(
                playerId,
                candidate.SeasonId,
                candidate.SeasonVersion,
                candidate.GrantId,
                candidate.Source,
                candidate.BaseXp,
                candidate.AwardedXp,
                candidate.Reason,
                candidate.OccurredAtUtc,
                candidate.BoostId,
                candidate.BoostMultiplier,
                seasonXpAfter,
                revisionAfter);

            await InsertGrantAsync(connection, transaction, grant, token).ConfigureAwait(false);
            await UpdateAccountAsync(
                connection, transaction, playerId, candidate.SeasonId,
                state.Revision, seasonXpAfter, revisionAfter, token).ConfigureAwait(false);

            return new SeasonXpGrantCommitResult(true, grant);
        }, IsolationLevel.ReadCommitted, cancellationToken);
    }

    private static async ValueTask<LockedSeason> LockSeasonAsync(
        DbConnection connection,
        DbTransaction transaction,
        string seasonId,
        int seasonVersion,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT starts_at_utc, ends_at_utc, closed_at_utc,
                (SELECT MAX(s2.definition_version)
                 FROM ano_progression_seasons s2
                 WHERE s2.season_id = s.season_id) AS latest_version
            FROM ano_progression_seasons s
            WHERE season_id = @season AND definition_version = @version
            LOCK IN SHARE MODE
            """;
        Add(command, "@season", seasonId);
        Add(command, "@version", seasonVersion);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new KeyNotFoundException($"Season '{seasonId}' version {seasonVersion} was not found.");

        var latestVersion = Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture);
        if (latestVersion != seasonVersion)
            throw new SeasonDefinitionConflictException(seasonId, seasonVersion);

        return new LockedSeason(
            Utc(reader.GetDateTime(0)),
            Utc(reader.GetDateTime(1)),
            reader.IsDBNull(2) ? null : Utc(reader.GetDateTime(2)));
    }

    private static async ValueTask EnsureAccountAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId playerId,
        SeasonXpGrantCandidate candidate,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_progression_season_accounts (
                player_steam_id, season_id, season_version, season_xp, revision, updated_at_utc)
            VALUES (@player, @season, @version, 0, 0, @updated)
            ON DUPLICATE KEY UPDATE player_steam_id = player_steam_id
            """;
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@season", candidate.SeasonId);
        Add(command, "@version", candidate.SeasonVersion);
        Add(command, "@updated", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async ValueTask<SeasonProgressionState> ReadForUpdateAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId playerId,
        SeasonXpGrantCandidate candidate,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT season_version, season_xp, revision
            FROM ano_progression_season_accounts
            WHERE player_steam_id = @player AND season_id = @season
            FOR UPDATE
            """;
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@season", candidate.SeasonId);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidOperationException("Season progression account row disappeared during grant.");

        var version = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
        if (version != candidate.SeasonVersion)
            throw new SeasonDefinitionConflictException(candidate.SeasonId, candidate.SeasonVersion);

        return new SeasonProgressionState(
            playerId,
            candidate.SeasonId,
            version,
            ReadNonnegativeInt64(reader.GetValue(1), "season_xp"),
            ReadNonnegativeInt64(reader.GetValue(2), "revision"));
    }

    private static async ValueTask<SeasonXpGrantRecord?> ReadGrantAsync(
        DbConnection connection,
        DbTransaction? transaction,
        PlayerId playerId,
        string seasonId,
        string grantId,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT season_version, source, base_xp, awarded_xp, reason,
                occurred_at_utc, boost_id, boost_multiplier,
                season_xp_after, account_revision_after
            FROM ano_progression_season_grants
            WHERE player_steam_id = @player
                AND season_id = @season
                AND grant_id = @grant
            """;
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@season", seasonId);
        Add(command, "@grant", grantId);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            return null;

        var sourceValue = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
        if (!Enum.IsDefined(typeof(ProgressionXpSource), sourceValue))
            throw new InvalidOperationException("Stored season XP grant has an invalid source.");

        return new SeasonXpGrantRecord(
            playerId,
            seasonId,
            Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
            grantId,
            (ProgressionXpSource)sourceValue,
            Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
            Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture),
            reader.GetString(4),
            Utc(reader.GetDateTime(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetDecimal(7),
            ReadNonnegativeInt64(reader.GetValue(8), "season_xp_after"),
            ReadNonnegativeInt64(reader.GetValue(9), "account_revision_after"));
    }

    private static async ValueTask InsertGrantAsync(
        DbConnection connection,
        DbTransaction transaction,
        SeasonXpGrantRecord grant,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_progression_season_grants (
                player_steam_id, season_id, season_version, grant_id,
                source, base_xp, awarded_xp, reason, occurred_at_utc,
                boost_id, boost_multiplier, season_xp_after,
                account_revision_after, created_at_utc)
            VALUES (
                @player, @season, @version, @grant,
                @source, @base, @awarded, @reason, @occurred,
                @boost, @multiplier, @seasonAfter,
                @revisionAfter, @created)
            """;
        Add(command, "@player", grant.PlayerId.SteamId64);
        Add(command, "@season", grant.SeasonId);
        Add(command, "@version", grant.SeasonVersion);
        Add(command, "@grant", grant.GrantId);
        Add(command, "@source", (int)grant.Source);
        Add(command, "@base", grant.BaseXp);
        Add(command, "@awarded", grant.AwardedXp);
        Add(command, "@reason", grant.Reason);
        Add(command, "@occurred", grant.OccurredAtUtc.UtcDateTime);
        Add(command, "@boost", grant.BoostId is null ? DBNull.Value : grant.BoostId);
        Add(command, "@multiplier", grant.BoostMultiplier);
        Add(command, "@seasonAfter", grant.SeasonXpAfter);
        Add(command, "@revisionAfter", grant.AccountRevisionAfter);
        Add(command, "@created", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async ValueTask UpdateAccountAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId playerId,
        string seasonId,
        long expectedRevision,
        long seasonXpAfter,
        long revisionAfter,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ano_progression_season_accounts
            SET season_xp = @xp,
                revision = @revision,
                updated_at_utc = @updated
            WHERE player_steam_id = @player
                AND season_id = @season
                AND revision = @expected
            """;
        Add(command, "@xp", seasonXpAfter);
        Add(command, "@revision", revisionAfter);
        Add(command, "@updated", DateTime.UtcNow);
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@season", seasonId);
        Add(command, "@expected", expectedRevision);
        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Season progression account revision changed unexpectedly.");
    }

    private static SeasonXpGrantCandidate NormalizeAndValidate(SeasonXpGrantCandidate candidate)
    {
        ValidateSeasonKey(candidate.SeasonId, candidate.SeasonVersion);
        ValidateGrantId(candidate.GrantId);
        if (!PrintableBounded(candidate.Reason, SeasonProgressionService.MaxReasonLength)
            || !Enum.IsDefined(candidate.Source)
            || candidate.BoostMultiplier is < 1m or > ProgressionDefinitionSnapshot.MaxBoostMultiplier
            || candidate.BoostId is not null && !PrintableBounded(candidate.BoostId, 64))
        {
            throw new ArgumentException("Season XP grant candidate is invalid.", nameof(candidate));
        }

        if (candidate.Source == ProgressionXpSource.Administration)
        {
            if (candidate.AwardedXp != candidate.BaseXp
                || candidate.BoostId is not null
                || candidate.BoostMultiplier != 1m)
            {
                throw new ArgumentException("Administrative season XP adjustments cannot be boosted.", nameof(candidate));
            }
        }
        else
        {
            if (candidate.BaseXp < 0)
                throw new ArgumentException("Non-administrative season XP cannot be negative.", nameof(candidate));
            var expected = checked((long)decimal.Truncate(candidate.BaseXp * candidate.BoostMultiplier));
            if (expected != candidate.AwardedXp)
                throw new ArgumentException("Awarded season XP does not match base XP and multiplier.", nameof(candidate));
        }

        return candidate with { OccurredAtUtc = NormalizeUtc(candidate.OccurredAtUtc) };
    }

    private static bool MatchesOriginalCandidate(
        SeasonXpGrantRecord existing,
        SeasonXpGrantCandidate candidate)
        => existing.SeasonVersion == candidate.SeasonVersion
            && existing.Source == candidate.Source
            && existing.BaseXp == candidate.BaseXp
            && string.Equals(existing.Reason, candidate.Reason, StringComparison.Ordinal)
            && existing.OccurredAtUtc == candidate.OccurredAtUtc;

    private static void ValidateSeasonKey(string? seasonId, int version)
    {
        ValidateSeasonId(seasonId);
        if (version < 1)
            throw new ArgumentException("Season version must be positive.", nameof(version));
    }

    private static void ValidateSeasonId(string? seasonId)
    {
        if (string.IsNullOrWhiteSpace(seasonId)
            || seasonId.Length > SeasonCatalogSnapshot.MaxSeasonIdLength
            || seasonId != seasonId.Trim()
            || seasonId.Any(character =>
                character > 0x7f
                || !(char.IsLetterOrDigit(character)
                    || character is '.' or '_' or '-')))
        {
            throw new ArgumentException("Season ID is invalid.", nameof(seasonId));
        }
    }

    private static void ValidateGrantId(string? grantId)
    {
        if (!PrintableBounded(grantId, SeasonProgressionService.MaxGrantIdLength)
            || grantId!.Any(character => character > 0x7f))
        {
            throw new ArgumentException(
                "Season XP grant ID must be printable ASCII without surrounding whitespace.",
                nameof(grantId));
        }
    }

    private static bool PrintableBounded(string? value, int maximum)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximum
            && value == value.Trim()
            && value.All(character => !char.IsControl(character));

    private static DateTimeOffset NormalizeUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.UtcDateTime.Ticks;
        ticks -= ticks % TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
    }

    private static DateTimeOffset Utc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static long ReadNonnegativeInt64(object value, string field)
    {
        try
        {
            var converted = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (converted < 0)
                throw new InvalidOperationException($"Stored progression field '{field}' cannot be negative.");
            return converted;
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException(
                $"Stored progression field '{field}' exceeds supported XP range.", exception);
        }
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record LockedSeason(
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc,
        DateTimeOffset? ClosedAtUtc);
}
