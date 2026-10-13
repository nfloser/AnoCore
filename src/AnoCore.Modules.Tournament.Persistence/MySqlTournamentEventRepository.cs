using System.Data;
using System.Data.Common;
using System.Text.Json;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Tournament.Persistence;

/// <summary>
/// Event-wide optimistic-concurrency store. Independent of each CS2 server's
/// live match controller; readers can load an event from any connected server.
/// </summary>
public sealed class MySqlTournamentEventRepository : ITournamentEventRepository
{
    private readonly IDatabase _database;

    public MySqlTournamentEventRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public ValueTask<TournamentStoredEvent> CreateAsync(
        TournamentEventDefinition definition, CancellationToken cancellationToken = default)
    {
        TournamentEventSnapshotValidator.Validate(definition, []);
        return _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ano_tournament_events
                    (event_id, definition_json, results_json, playoff_results_json, revision, updated_at_utc)
                VALUES (@id, @definition, @results, '[]', 1, UTC_TIMESTAMP(6))
                """;
            Add(command, "@id", definition.Id);
            Add(command, "@definition", JsonSerializer.Serialize(definition));
            Add(command, "@results", "[]");
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return new TournamentStoredEvent(definition, [], 1);
        }, cancellationToken: cancellationToken);
    }

    public ValueTask<TournamentStoredEvent?> LoadAsync(
        string eventId, CancellationToken cancellationToken = default)
    {
        RequireId(eventId);
        return _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT definition_json, results_json, revision, playoff_results_json
                FROM ano_tournament_events WHERE event_id = @id
                """;
            Add(command, "@id", eventId);
            return await ReadOneAsync(command, eventId, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public ValueTask<TournamentStoredEvent?> LoadActiveAsync(
        CancellationToken cancellationToken = default)
        => _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT e.definition_json, e.results_json, e.revision, e.event_id, e.playoff_results_json
                FROM ano_tournament_event_runtime AS r
                JOIN ano_tournament_events AS e ON e.event_id = r.active_event_id
                WHERE r.singleton_id = 1
                """;
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                return null;
            var eventId = reader.GetString(3);
            return Deserialize(eventId, reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(4));
        }, cancellationToken);

    public ValueTask<TournamentStoredEvent> SaveAsync(
        string eventId, long expectedRevision, TournamentEventDefinition definition,
        IReadOnlyCollection<TournamentGroupResult> confirmedResults,
        CancellationToken cancellationToken = default)
    {
        RequireId(eventId);
        if (expectedRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        ArgumentNullException.ThrowIfNull(definition);
        if (!string.Equals(eventId, definition.Id, StringComparison.Ordinal))
            throw new ArgumentException("The event ID cannot change across revisions.", nameof(definition));
        TournamentEventSnapshotValidator.Validate(definition, confirmedResults);
        var results = confirmedResults.ToArray();

        return _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await using var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT playoff_results_json, revision, definition_json, results_json FROM ano_tournament_events WHERE event_id = @id FOR UPDATE";
            Add(read, "@id", eventId);
            IReadOnlyList<TournamentBracketResult> playoffs;
            await using (var reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.GetInt64(1) != expectedRevision)
                    throw new TournamentEventConcurrencyException(eventId, expectedRevision);
                playoffs = DeserializePlayoffs(reader.GetString(0));
                if (playoffs.Count > 0)
                {
                    var previous = JsonSerializer.Deserialize<TournamentEventDefinition>(reader.GetString(2))
                        ?? throw new InvalidDataException("Missing previous event definition.");
                    var previousGroups = JsonSerializer.Deserialize<List<TournamentGroupResult>>(reader.GetString(3))
                        ?? throw new InvalidDataException("Missing previous group results.");
                    if (previous.GroupStageEnabled != definition.GroupStageEnabled || previous.PlayoffsEnabled != definition.PlayoffsEnabled
                        || previous.PlayoffFormat != definition.PlayoffFormat || previous.GrandFinalReset != definition.GrandFinalReset
                        || previous.PlayoffBestOf != definition.PlayoffBestOf || previous.GroupFormat != definition.GroupFormat
                        || previous.GroupBestOf != definition.GroupBestOf || previous.SwissRounds != definition.SwissRounds)
                        throw new ArgumentException("Cannot change stage rules after confirmed playoffs.", nameof(definition));
                    var before = TournamentEventPlayoffs.ResolveEntrants(previous, previousGroups);
                    var after = TournamentEventPlayoffs.ResolveEntrants(definition, results);
                    if (before is null || after is null || !before.SequenceEqual(after))
                        throw new ArgumentException("Cannot change playoff entrants or seeds after confirmed results.", nameof(definition));
                }
            }
            _ = TournamentEventPlayoffs.Progress(definition, results, playoffs);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE ano_tournament_events
                SET definition_json = @definition, results_json = @results,
                    revision = revision + 1, updated_at_utc = UTC_TIMESTAMP(6)
                WHERE event_id = @id AND revision = @expected
                """;
            Add(command, "@id", eventId);
            Add(command, "@expected", expectedRevision);
            Add(command, "@definition", JsonSerializer.Serialize(definition));
            Add(command, "@results", JsonSerializer.Serialize(results));
            var updated = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            if (updated != 1)
                throw new TournamentEventConcurrencyException(eventId, expectedRevision);
            return new TournamentStoredEvent(definition, results, checked(expectedRevision + 1)) { PlayoffResults = playoffs };
        }, cancellationToken: cancellationToken);
    }

    public async ValueTask<TournamentStoredEvent> SavePlayoffsAsync(string eventId, long expectedRevision,
        IReadOnlyCollection<TournamentBracketResult> results, CancellationToken cancellationToken = default)
    {
        RequireId(eventId);
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        ArgumentNullException.ThrowIfNull(results);
        var snapshot = await LoadAsync(eventId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || snapshot.Revision != expectedRevision)
            throw new TournamentEventConcurrencyException(eventId, expectedRevision);
        var playoffs = Array.AsReadOnly(results.ToArray());
        _ = TournamentEventPlayoffs.Progress(snapshot.Definition, snapshot.ConfirmedResults, playoffs);
        return await _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE ano_tournament_events SET playoff_results_json = @results,
                    revision = revision + 1, updated_at_utc = UTC_TIMESTAMP(6)
                WHERE event_id = @id AND revision = @expected
                """;
            Add(command, "@id", eventId);
            Add(command, "@expected", expectedRevision);
            Add(command, "@results", JsonSerializer.Serialize(playoffs));
            if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new TournamentEventConcurrencyException(eventId, expectedRevision);
            return snapshot with { PlayoffResults = playoffs, Revision = checked(expectedRevision + 1) };
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public ValueTask ActivateAsync(string eventId, CancellationToken cancellationToken = default)
    {
        RequireId(eventId);
        return ActivateInternalAsync(eventId, cancellationToken);
    }

    private async ValueTask ActivateInternalAsync(string eventId, CancellationToken cancellationToken)
    {
        await _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await using var exists = connection.CreateCommand();
            exists.Transaction = transaction;
            exists.CommandText = "SELECT event_id FROM ano_tournament_events WHERE event_id = @id";
            Add(exists, "@id", eventId);
            if (await exists.ExecuteScalarAsync(token).ConfigureAwait(false) is null)
                throw new KeyNotFoundException($"Tournament event '{eventId}' does not exist.");

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE ano_tournament_event_runtime SET active_event_id = @id
                WHERE singleton_id = 1
                """;
            Add(command, "@id", eventId);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, isolationLevel: IsolationLevel.Serializable, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<TournamentStoredEvent?> ReadOneAsync(
        DbCommand command, string eventId, CancellationToken token)
    {
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            return null;
        return Deserialize(eventId, reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(3));
    }

    private static TournamentStoredEvent Deserialize(
        string eventId, string definitionJson, string resultsJson, long revision, string playoffJson)
    {
        var definition = JsonSerializer.Deserialize<TournamentEventDefinition>(definitionJson)
            ?? throw new InvalidDataException("Missing tournament definition in database.");
        var results = JsonSerializer.Deserialize<List<TournamentGroupResult>>(resultsJson)
            ?? throw new InvalidDataException("Missing tournament results in database.");
        if (revision < 1 || !string.Equals(eventId, definition.Id, StringComparison.Ordinal))
            throw new InvalidDataException("Stored tournament identity/revision is invalid.");
        TournamentEventSnapshotValidator.Validate(definition, results);
        var playoffs = DeserializePlayoffs(playoffJson);
        _ = TournamentEventPlayoffs.Progress(definition, results, playoffs);
        return new TournamentStoredEvent(definition, results.AsReadOnly(), revision) { PlayoffResults = playoffs };
    }

    private static IReadOnlyList<TournamentBracketResult> DeserializePlayoffs(string json)
        => (JsonSerializer.Deserialize<List<TournamentBracketResult>>(json)
            ?? throw new InvalidDataException("Missing playoff results in database.")).AsReadOnly();

    private static void RequireId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 48 ||
            id.Any(character => !(character is >= 'a' and <= 'z'
                || character is >= '0' and <= '9' || character is '-' or '_')) ||
            id[0] is '-' or '_')
            throw new ArgumentException("A valid lowercase tournament event ID is required.", nameof(id));
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
