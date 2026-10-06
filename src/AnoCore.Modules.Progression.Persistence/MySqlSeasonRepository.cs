using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class MySqlSeasonRepository : ISeasonRepository
{
    private readonly IDatabase _database;

    public MySqlSeasonRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public ValueTask<IReadOnlyList<PersistedSeason>> ListEffectiveAsync(
        CancellationToken cancellationToken = default)
        => _database.WithConnectionAsync(async (connection, token) =>
        {
            var seasons = await ReadEffectiveAsync(
                connection, transaction: null, token).ConfigureAwait(false);
            _ = SeasonCatalogSnapshot.Create(
                seasons.Select(value => value.Definition));
            return seasons;
        }, cancellationToken);

    public ValueTask<PersistedSeason?> ReadAsync(
        string seasonId,
        int version,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(seasonId, version);
        return _database.WithConnectionAsync(
            (connection, token) => ReadStoredAsync(
                connection, transaction: null, seasonId, version, forUpdate: false, token),
            cancellationToken);
    }

    public ValueTask<SeasonAcceptResult> AcceptAsync(
        SeasonDefinition definition,
        DateTimeOffset acceptedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var canonical = Canonicalize(definition);
        var acceptedAt = NormalizeUtc(acceptedAtUtc);

        return _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await LockRuntimeAsync(connection, transaction, token).ConfigureAwait(false);

            var existing = await ReadStoredAsync(
                connection, transaction, canonical.Id, canonical.Version,
                forUpdate: true, token).ConfigureAwait(false);
            if (existing is not null)
            {
                if (!SameDefinition(existing.Definition, canonical))
                    throw new SeasonDefinitionConflictException(
                        canonical.Id, canonical.Version);
                return new SeasonAcceptResult(false, existing);
            }

            if (acceptedAt >= canonical.StartsAtUtc)
                throw new SeasonDefinitionConflictException(
                    canonical.Id, canonical.Version);

            var latest = await ReadLatestAsync(
                connection, transaction, canonical.Id, token).ConfigureAwait(false);
            if (latest is not null)
            {
                if (canonical.Version <= latest.Definition.Version
                    || latest.ClosedAtUtc is not null
                    || acceptedAt >= latest.Definition.StartsAtUtc)
                {
                    throw new SeasonDefinitionConflictException(
                        canonical.Id, canonical.Version);
                }
            }

            var effective = await ReadEffectiveAsync(
                connection, transaction, token).ConfigureAwait(false);
            foreach (var accepted in effective)
            {
                if (string.Equals(
                    accepted.Definition.Id,
                    canonical.Id,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Overlaps(canonical, accepted.Definition))
                    throw new SeasonOverlapException(
                        canonical.Id, accepted.Definition.Id);
            }

            var stored = new PersistedSeason(canonical, acceptedAt, null);
            await InsertAsync(
                connection, transaction, stored, token).ConfigureAwait(false);
            return new SeasonAcceptResult(true, stored);
        }, cancellationToken: cancellationToken);
    }

    public ValueTask<PersistedSeason> CloseAsync(
        string seasonId,
        int version,
        DateTimeOffset closedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(seasonId, version);
        var closedAt = NormalizeUtc(closedAtUtc);

        return _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await LockRuntimeAsync(connection, transaction, token).ConfigureAwait(false);
            var stored = await ReadStoredAsync(
                connection, transaction, seasonId, version,
                forUpdate: true, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException(
                    $"Season '{seasonId}' version {version} was not found.");

            if (stored.ClosedAtUtc is not null)
                return stored;

            var latest = await ReadLatestAsync(
                connection, transaction, stored.Definition.Id, token)
                .ConfigureAwait(false);
            if (latest is null
                || latest.Definition.Version != stored.Definition.Version)
            {
                throw new SeasonDefinitionConflictException(
                    stored.Definition.Id, stored.Definition.Version);
            }

            if (closedAt < stored.Definition.EndsAtUtc)
                throw new InvalidOperationException(
                    "A season cannot close before its configured end.");

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE ano_progression_seasons
                SET closed_at_utc = @closed
                WHERE season_id = @season
                    AND definition_version = @version
                    AND closed_at_utc IS NULL
                """;
            Add(command, "@closed", closedAt.UtcDateTime);
            Add(command, "@season", stored.Definition.Id);
            Add(command, "@version", stored.Definition.Version);
            if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException(
                    "Season closure changed unexpectedly.");

            return stored with { ClosedAtUtc = closedAt };
        }, cancellationToken: cancellationToken);
    }

    private static async ValueTask LockRuntimeAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT singleton_id
            FROM ano_progression_season_runtime
            WHERE singleton_id = 1
            FOR UPDATE
            """;
        var value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        if (value is null or DBNull)
            throw new InvalidOperationException(
                "Progression season runtime singleton is missing.");
    }

    private static async ValueTask<PersistedSeason?> ReadStoredAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string seasonId,
        int version,
        bool forUpdate,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT season_id, definition_version, name,
                starts_at_utc, ends_at_utc, accepted_at_utc, closed_at_utc
            FROM ano_progression_seasons
            WHERE season_id = @season AND definition_version = @version
            """ + (forUpdate ? " FOR UPDATE" : string.Empty);
        Add(command, "@season", seasonId);
        Add(command, "@version", version);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            return null;
        return ReadSeason(reader);
    }

    private static async ValueTask<PersistedSeason?> ReadLatestAsync(
        DbConnection connection,
        DbTransaction transaction,
        string seasonId,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT season_id, definition_version, name,
                starts_at_utc, ends_at_utc, accepted_at_utc, closed_at_utc
            FROM ano_progression_seasons
            WHERE season_id = @season
            ORDER BY definition_version DESC
            LIMIT 1
            """;
        Add(command, "@season", seasonId);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false)
            ? ReadSeason(reader)
            : null;
    }

    private static async ValueTask<IReadOnlyList<PersistedSeason>> ReadEffectiveAsync(
        DbConnection connection,
        DbTransaction? transaction,
        CancellationToken token)
    {
        var result = new List<PersistedSeason>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT s.season_id, s.definition_version, s.name,
                s.starts_at_utc, s.ends_at_utc, s.accepted_at_utc, s.closed_at_utc
            FROM ano_progression_seasons AS s
            INNER JOIN (
                SELECT season_id, MAX(definition_version) AS definition_version
                FROM ano_progression_seasons
                GROUP BY season_id
            ) AS latest
                ON latest.season_id = s.season_id
                AND latest.definition_version = s.definition_version
            ORDER BY s.starts_at_utc, s.season_id
            """;
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(ReadSeason(reader));
        return result.AsReadOnly();
    }

    private static async ValueTask InsertAsync(
        DbConnection connection,
        DbTransaction transaction,
        PersistedSeason season,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_progression_seasons (
                season_id, definition_version, name,
                starts_at_utc, ends_at_utc, accepted_at_utc, closed_at_utc)
            VALUES (
                @season, @version, @name,
                @starts, @ends, @accepted, NULL)
            """;
        Add(command, "@season", season.Definition.Id);
        Add(command, "@version", season.Definition.Version);
        Add(command, "@name", season.Definition.Name);
        Add(command, "@starts", season.Definition.StartsAtUtc.UtcDateTime);
        Add(command, "@ends", season.Definition.EndsAtUtc.UtcDateTime);
        Add(command, "@accepted", season.AcceptedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static PersistedSeason ReadSeason(DbDataReader reader)
    {
        var version = Convert.ToInt64(
            reader.GetValue(1), CultureInfo.InvariantCulture);
        if (version is < 1 or > int.MaxValue)
            throw new InvalidOperationException(
                "Stored season definition version is invalid.");

        var definition = new SeasonDefinition(
            reader.GetString(0),
            (int)version,
            reader.GetString(2),
            Utc(reader.GetDateTime(3)),
            Utc(reader.GetDateTime(4)));
        _ = SeasonCatalogSnapshot.Create([definition]);

        return new PersistedSeason(
            definition,
            Utc(reader.GetDateTime(5)),
            reader.IsDBNull(6) ? null : Utc(reader.GetDateTime(6)));
    }

    private static SeasonDefinition Canonicalize(SeasonDefinition definition)
    {
        _ = SeasonCatalogSnapshot.Create([definition]);
        var canonical = definition with
        {
            StartsAtUtc = NormalizeUtc(definition.StartsAtUtc),
            EndsAtUtc = NormalizeUtc(definition.EndsAtUtc),
        };
        _ = SeasonCatalogSnapshot.Create([canonical]);
        return canonical;
    }

    private static void ValidateKey(string? seasonId, int version)
    {
        if (!ValidIdentifier(seasonId))
            throw new ArgumentException("Season ID is invalid.", nameof(seasonId));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version));
    }

    private static bool SameDefinition(
        SeasonDefinition left,
        SeasonDefinition right)
        => string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase)
            && left.Version == right.Version
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && left.StartsAtUtc == right.StartsAtUtc
            && left.EndsAtUtc == right.EndsAtUtc;

    private static bool Overlaps(
        SeasonDefinition left,
        SeasonDefinition right)
        => left.StartsAtUtc < right.EndsAtUtc
            && right.StartsAtUtc < left.EndsAtUtc;

    private static DateTimeOffset NormalizeUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.UtcDateTime.Ticks;
        ticks -= ticks % TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
    }

    private static DateTimeOffset Utc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static bool ValidIdentifier(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= SeasonCatalogSnapshot.MaxSeasonIdLength
            && value == value.Trim()
            && value.All(character =>
                character <= 0x7f
                && (char.IsLetterOrDigit(character)
                    || character is '.' or '_' or '-'));

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
