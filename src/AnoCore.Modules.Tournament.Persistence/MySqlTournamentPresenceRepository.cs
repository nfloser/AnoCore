using System.Data.Common;
using System.Text.Json;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Tournament.Persistence;

/// <summary>DB-clock leases fence duplicate server IDs; whole snapshots replace disconnected sessions.</summary>
public sealed class MySqlTournamentPresenceRepository(IDatabase database) : ITournamentPresenceRepository
{
    public ValueTask PublishAsync(string serverId, Guid instanceId, IReadOnlyCollection<TournamentPlayerPresence> players,
        CancellationToken cancellationToken = default)
    {
        TournamentPresenceValidation.RequireValid(serverId, instanceId, players);
        var json = JsonSerializer.Serialize(players.OrderBy(player => player.SteamId).ToArray());
        return PublishCoreAsync(serverId, instanceId, json, cancellationToken);
    }

    private async ValueTask PublishCoreAsync(string serverId, Guid instanceId, string json, CancellationToken cancellationToken)
    {
        await database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await using (var seed = connection.CreateCommand())
            {
                seed.Transaction = transaction;
                seed.CommandText = """
                    INSERT INTO ano_tournament_server_presence (server_id, instance_id, players_json, expires_at_utc)
                    VALUES (@id, @instance, '[]', '1970-01-01')
                    ON DUPLICATE KEY UPDATE server_id = server_id
                    """;
                Add(seed, "@id", serverId); Add(seed, "@instance", instanceId.ToString("D"));
                await seed.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            await using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT instance_id, expires_at_utc > UTC_TIMESTAMP(6) FROM ano_tournament_server_presence WHERE server_id = @id FOR UPDATE";
                Add(read, "@id", serverId);
                await using var reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new InvalidDataException("Missing server lease.");
                if (Guid.Parse(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!) != instanceId && Convert.ToBoolean(reader.GetValue(1)))
                    throw new TournamentServerIdentityConflictException(serverId);
            }
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE ano_tournament_server_presence SET instance_id = @instance, players_json = @players,
                    expires_at_utc = UTC_TIMESTAMP(6) + INTERVAL 30 SECOND WHERE server_id = @id
                """;
            Add(update, "@id", serverId); Add(update, "@instance", instanceId.ToString("D")); Add(update, "@players", json);
            await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<TournamentServerPresence>> LoadOnlineAsync(CancellationToken cancellationToken = default)
        => database.WithConnectionAsync<IReadOnlyList<TournamentServerPresence>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT server_id, instance_id, players_json, expires_at_utc FROM ano_tournament_server_presence
                WHERE expires_at_utc > UTC_TIMESTAMP(6) ORDER BY server_id LIMIT 129
                """;
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            var servers = new List<TournamentServerPresence>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var id = reader.GetString(0);
                var instance = Guid.Parse(Convert.ToString(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture)!);
                var players = JsonSerializer.Deserialize<List<TournamentPlayerPresence>>(reader.GetString(2))
                    ?? throw new InvalidDataException("Missing presence snapshot.");
                TournamentPresenceValidation.RequireValid(id, instance, players);
                servers.Add(new(id, instance, players.AsReadOnly(),
                    new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc))));
            }
            if (servers.Count > 128) throw new InvalidDataException("Presence exceeds the supported 128-server bound.");
            return servers.AsReadOnly();
        }, cancellationToken);

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
    }
}
