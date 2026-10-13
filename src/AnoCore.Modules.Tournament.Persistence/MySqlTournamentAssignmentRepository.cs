using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Tournament.Persistence;

/// <summary>Event-row serialization prevents duplicate fixtures and simultaneous team assignments.</summary>
public sealed class MySqlTournamentAssignmentRepository(IDatabase database) : ITournamentMatchAssignmentRepository
{
    private const string Columns = "assignment_id,event_id,fixture_id,is_playoff,home_team_id,away_team_id,server_id,instance_id,best_of,maps_json,state";
    public ValueTask<TournamentMatchClaim?> ClaimNextAsync(string serverId, Guid instanceId, TournamentMatchZyConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var errors = TournamentMatchZyConfiguration.Validate(configuration);
        if (errors.Count > 0 || !configuration.Enabled || !TournamentPresenceValidation.ValidServerId(serverId) || instanceId == Guid.Empty)
            throw new ArgumentException("Match assignment requires enabled validated configuration and server identity.");
        return database.InTransactionAsync<TournamentMatchClaim?>(async (connection, transaction, token) =>
        {
            string eventId;
            await using (var active = Command(connection, transaction, "SELECT active_event_id FROM ano_tournament_event_runtime WHERE singleton_id=1 FOR UPDATE"))
            {
                eventId = await active.ExecuteScalarAsync(token).ConfigureAwait(false) as string ?? "";
                if (eventId.Length == 0) return null;
            }
            TournamentStoredEvent stored;
            await using (var read = Command(connection, transaction, "SELECT definition_json,results_json,playoff_results_json,revision FROM ano_tournament_events WHERE event_id=@event FOR UPDATE"))
            {
                Add(read, "@event", eventId);
                await using var reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
                stored = ReadStored(reader);
            }
            var players = await RequireLease(connection, transaction, serverId, instanceId, token).ConfigureAwait(false);
            var online = players.Select(player => player.SteamId).ToHashSet();
            await using (var cooldown = Command(connection, transaction, "SELECT assignment_id FROM ano_tournament_assignments WHERE server_id=@server AND state IN ('completed','canceled') AND updated_at_utc>UTC_TIMESTAMP(6)-INTERVAL 30 SECOND LIMIT 1"))
            {
                Add(cooldown, "@server", serverId);
                if (await cooldown.ExecuteScalarAsync(token).ConfigureAwait(false) is not null) return null;
            }
            await using (var native = Command(connection, transaction, "SELECT active_match_id FROM ano_tournament_server_runtime WHERE server_id=@server"))
            {
                Add(native, "@server", serverId);
                if (await native.ExecuteScalarAsync(token).ConfigureAwait(false) is { } nativeId && nativeId is not DBNull) return null;
            }
            var open = new List<TournamentMatchAssignment>();
            await using (var read = Command(connection, transaction, "SELECT " + Columns + " FROM ano_tournament_assignments WHERE state IN ('pending','dispatched') AND (event_id=@event OR server_id=@server) FOR UPDATE"))
            {
                Add(read, "@event", stored.Definition.Id); Add(read, "@server", serverId);
                await using var reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false)) open.Add(ReadAssignment(reader));
            }
            if (open.Any(match => match.ServerId == serverId)) return null;
            var busy = open.SelectMany(match => new[] { match.HomeTeamId, match.AwayTeamId }).ToHashSet(StringComparer.Ordinal);
            // A SteamID appearing on two live servers is ambiguous and cannot authorize a claim.
            var counts = new Dictionary<ulong, int>();
            await using (var read = Command(connection, transaction, "SELECT players_json FROM ano_tournament_server_presence WHERE expires_at_utc>UTC_TIMESTAMP(6)"))
            {
                await using var reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    foreach (var player in JsonSerializer.Deserialize<List<TournamentPlayerPresence>>(reader.GetString(0))!)
                        counts[player.SteamId] = counts.GetValueOrDefault(player.SteamId) + 1;
            }
            var teams = stored.Definition.Teams.ToDictionary(team => team.Id, StringComparer.Ordinal);
            bool Present(string id) => teams[id].Starters.All(steam => online.Contains(steam) && counts.GetValueOrDefault(steam) == 1);
            var fixture = TournamentMatchAssignmentPlan.Ready(stored, configuration).FirstOrDefault(fixture =>
                !busy.Contains(fixture.HomeTeamId) && !busy.Contains(fixture.AwayTeamId)
                && !open.Any(match => match.FixtureId == fixture.Id && match.EventId == stored.Definition.Id)
                && Present(fixture.HomeTeamId) && Present(fixture.AwayTeamId));
            if (fixture is null) return null;
            var maps = configuration.Maps.Take(fixture.BestOf).ToArray();
            var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            await using var insert = Command(connection, transaction, """
                INSERT INTO ano_tournament_assignments(event_id,fixture_id,server_id,instance_id,home_team_id,away_team_id,is_playoff,
                    best_of,maps_json,definition_json,token_hash,state,results_json,updated_at_utc)
                VALUES(@event,@fixture,@server,@instance,@home,@away,@playoff,@bo,@maps,@definition,@hash,'pending',@results,UTC_TIMESTAMP(6))
                """);
            Add(insert, "@event", stored.Definition.Id); Add(insert, "@fixture", fixture.Id); Add(insert, "@server", serverId);
            Add(insert, "@instance", instanceId.ToString("D")); Add(insert, "@home", fixture.HomeTeamId); Add(insert, "@away", fixture.AwayTeamId);
            Add(insert, "@playoff", fixture.IsPlayoff); Add(insert, "@bo", fixture.BestOf); Add(insert, "@maps", JsonSerializer.Serialize(maps));
            Add(insert, "@definition", JsonSerializer.Serialize(stored.Definition)); Add(insert, "@hash", Hash(secret));
            Add(insert, "@results", JsonSerializer.Serialize(new TournamentMatchZyResultState()));
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await using var idCommand = Command(connection, transaction, "SELECT LAST_INSERT_ID()");
            var id = Convert.ToInt64(await idCommand.ExecuteScalarAsync(token).ConfigureAwait(false));
            await Audit(connection, transaction, id, "claimed", serverId, token).ConfigureAwait(false);
            return new(new(id, stored.Definition.Id, fixture.Id, fixture.IsPlayoff, fixture.HomeTeamId, fixture.AwayTeamId,
                serverId, instanceId, fixture.BestOf, maps, "pending"), stored.Definition, secret);
        }, cancellationToken: cancellationToken);
    }

    public ValueTask<bool> MarkDispatchedAsync(long assignmentId, string serverId, Guid instanceId, CancellationToken cancellationToken = default)
        => database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await RequireLease(connection, transaction, serverId, instanceId, token).ConfigureAwait(false);
            await using var update = Command(connection, transaction, """
                UPDATE ano_tournament_assignments SET state='dispatched',updated_at_utc=UTC_TIMESTAMP(6)
                WHERE assignment_id=@id AND server_id=@server AND instance_id=@instance AND state='pending'
                """);
            Add(update, "@id", assignmentId); Add(update, "@server", serverId); Add(update, "@instance", instanceId.ToString("D"));
            var changed = await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
            if (changed) await Audit(connection, transaction, assignmentId, "dispatched", serverId, token).ConfigureAwait(false);
            return changed;
        }, cancellationToken: cancellationToken);

    public ValueTask<IReadOnlyList<TournamentMatchAssignment>> LoadOpenAsync(CancellationToken cancellationToken = default)
        => database.WithConnectionAsync<IReadOnlyList<TournamentMatchAssignment>>(async (connection, token) =>
        {
            await using var read = Command(connection, null, "SELECT " + Columns + " FROM ano_tournament_assignments WHERE state IN ('pending','dispatched') ORDER BY assignment_id LIMIT 129");
            await using var reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false);
            var assignments = new List<TournamentMatchAssignment>();
            while (await reader.ReadAsync(token).ConfigureAwait(false)) assignments.Add(ReadAssignment(reader));
            if (assignments.Count > 128) throw new InvalidDataException("Open assignment bound exceeded.");
            return assignments;
        }, cancellationToken);

    public ValueTask<bool> AuthenticateAsync(long assignmentId, string token, CancellationToken cancellationToken = default)
        => database.WithConnectionAsync(async (connection, cancellation) =>
        {
            if (token.Length != 64 || token.Any(c => !Uri.IsHexDigit(c))) return false;
            await using var read = Command(connection, null, "SELECT token_hash FROM ano_tournament_assignments WHERE assignment_id=@id AND state <> 'canceled'");
            Add(read, "@id", assignmentId);
            var hash = await read.ExecuteScalarAsync(cancellation).ConfigureAwait(false) as string;
            return hash is not null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(Hash(token)));
        }, cancellationToken);

    public ValueTask<bool> ApplyEventAsync(long assignmentId, string payload, CancellationToken cancellationToken = default)
        => database.InTransactionAsync(async (connection, transaction, token) =>
        {
            var stored = await LockEvent(connection, transaction, assignmentId, token).ConfigureAwait(false);
            TournamentMatchAssignment assignment;
            TournamentMatchZyResultState previous;
            await using (var read = Command(connection, transaction, "SELECT " + Columns + ",results_json FROM ano_tournament_assignments WHERE assignment_id=@id FOR UPDATE"))
            {
                Add(read, "@id", assignmentId);
                await using var reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new KeyNotFoundException("Unknown assignment.");
                assignment = ReadAssignment(reader);
                previous = JsonSerializer.Deserialize<TournamentMatchZyResultState>(reader.GetString(11))!;
            }
            if (assignment.State is "pending" or "canceled") throw new ArgumentException("Assignment is not dispatched.");
            var next = TournamentMatchZy.Apply(previous, payload, assignmentId, assignment.BestOf);
            var completed = TournamentMatchZy.Completed(next, assignment.BestOf);
            if (JsonSerializer.Serialize(next) == JsonSerializer.Serialize(previous)) return completed is not null;
            if (assignment.State == "completed") return true; // Apply still rejects contradictory replayed results.
            if (completed is not null)
            {
                if (assignment.IsPlayoff)
                {
                    var winner = completed.HomeMaps > completed.AwayMaps ? assignment.HomeTeamId : assignment.AwayTeamId;
                    var results = stored.PlayoffResults.Append(new TournamentBracketResult(assignment.FixtureId, winner,
                        Math.Max(completed.HomeMaps, completed.AwayMaps), Math.Min(completed.HomeMaps, completed.AwayMaps))).ToArray();
                    _ = TournamentEventPlayoffs.Progress(stored.Definition, stored.ConfirmedResults, results);
                    await WriteResults(connection, transaction, stored.Definition.Id, "playoff_results_json", JsonSerializer.Serialize(results), token).ConfigureAwait(false);
                }
                else
                {
                    var results = stored.ConfirmedResults.Append(new TournamentGroupResult(assignment.FixtureId,
                        completed.HomeMaps, completed.AwayMaps, completed.HomeRounds, completed.AwayRounds)).ToArray();
                    TournamentEventSnapshotValidator.Validate(stored.Definition, results);
                    _ = TournamentEventPlayoffs.Progress(stored.Definition, results, stored.PlayoffResults);
                    await WriteResults(connection, transaction, stored.Definition.Id, "results_json", JsonSerializer.Serialize(results), token).ConfigureAwait(false);
                }
            }
            await using var update = Command(connection, transaction, "UPDATE ano_tournament_assignments SET results_json=@results,state=@state,updated_at_utc=UTC_TIMESTAMP(6) WHERE assignment_id=@id");
            Add(update, "@results", JsonSerializer.Serialize(next)); Add(update, "@state", completed is null ? "dispatched" : "completed"); Add(update, "@id", assignmentId);
            await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await Audit(connection, transaction, assignmentId, completed is null ? "result-accepted" : "completed", Hash(payload), token).ConfigureAwait(false);
            return completed is not null;
        }, cancellationToken: cancellationToken);

    public async ValueTask CancelAsync(long assignmentId, string serverId, Guid instanceId, CancellationToken cancellationToken = default)
    {
        await database.InTransactionAsync(async (connection, transaction, token) =>
        {
            _ = await LockEvent(connection, transaction, assignmentId, token).ConfigureAwait(false);
            await RequireLease(connection, transaction, serverId, instanceId, token).ConfigureAwait(false);
            await using var update = Command(connection, transaction, "UPDATE ano_tournament_assignments SET state='canceled',updated_at_utc=UTC_TIMESTAMP(6) WHERE assignment_id=@id AND server_id=@server AND state IN ('pending','dispatched')");
            Add(update, "@id", assignmentId); Add(update, "@server", serverId);
            if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw new ArgumentException("Assignment is completed, canceled or owned by another server.");
            await Audit(connection, transaction, assignmentId, "canceled", serverId, token).ConfigureAwait(false);
            return true;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<TournamentStoredEvent> LockEvent(DbConnection connection, DbTransaction transaction, long id, CancellationToken token)
    {
        string eventId;
        await using (var lookup = Command(connection, transaction, "SELECT event_id FROM ano_tournament_assignments WHERE assignment_id=@id"))
        {
            Add(lookup, "@id", id);
            eventId = await lookup.ExecuteScalarAsync(token).ConfigureAwait(false) as string
                ?? throw new KeyNotFoundException("Unknown assignment.");
        }
        await using var read = Command(connection, transaction, "SELECT definition_json,results_json,playoff_results_json,revision FROM ano_tournament_events WHERE event_id=@event FOR UPDATE");
        Add(read, "@event", eventId);
        await using var reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new KeyNotFoundException("Unknown assignment.");
        return ReadStored(reader);
    }
    private static TournamentStoredEvent ReadStored(DbDataReader reader) => new(
        JsonSerializer.Deserialize<TournamentEventDefinition>(reader.GetString(0))!,
        JsonSerializer.Deserialize<List<TournamentGroupResult>>(reader.GetString(1))!, reader.GetInt64(3))
    { PlayoffResults = JsonSerializer.Deserialize<List<TournamentBracketResult>>(reader.GetString(2))! };
    private static TournamentMatchAssignment ReadAssignment(DbDataReader reader) => new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
        reader.GetBoolean(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), ReadGuid(reader, 7), reader.GetInt32(8),
        JsonSerializer.Deserialize<List<string>>(reader.GetString(9))!, reader.GetString(10));
    private static async ValueTask<IReadOnlyList<TournamentPlayerPresence>> RequireLease(DbConnection connection, DbTransaction transaction, string server, Guid instance, CancellationToken token)
    {
        await using var read = Command(connection, transaction, "SELECT instance_id,players_json FROM ano_tournament_server_presence WHERE server_id=@server AND expires_at_utc>UTC_TIMESTAMP(6) FOR UPDATE");
        Add(read, "@server", server);
        await using var reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false) || ReadGuid(reader, 0) != instance)
            throw new TournamentServerIdentityConflictException(server);
        return JsonSerializer.Deserialize<List<TournamentPlayerPresence>>(reader.GetString(1))!;
    }
    private static async ValueTask WriteResults(DbConnection connection, DbTransaction transaction, string eventId, string column, string json, CancellationToken token)
    {
        await using var update = Command(connection, transaction, $"UPDATE ano_tournament_events SET {column}=@results,revision=revision+1,updated_at_utc=UTC_TIMESTAMP(6) WHERE event_id=@event");
        Add(update, "@results", json); Add(update, "@event", eventId);
        await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
    private static async ValueTask Audit(DbConnection connection, DbTransaction transaction, long id, string action, string detail, CancellationToken token)
    {
        await using var insert = Command(connection, transaction, "INSERT INTO ano_tournament_assignment_audit(assignment_id,action,detail,occurred_at_utc) VALUES(@id,@action,@detail,UTC_TIMESTAMP(6))");
        Add(insert, "@id", id); Add(insert, "@action", action); Add(insert, "@detail", detail);
        await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
    private static Guid ReadGuid(DbDataReader reader, int ordinal) => reader.GetValue(ordinal) is Guid guid
        ? guid : Guid.Parse(Convert.ToString(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture)!);
    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql)
    { var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; return command; }
    private static void Add(DbCommand command, string name, object value)
    { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }
}
