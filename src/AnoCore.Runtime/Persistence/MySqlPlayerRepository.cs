using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Persistence;

public sealed class MySqlPlayerRepository : IPlayerRepository
{
    private readonly IDatabase _database;

    public MySqlPlayerRepository(IDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public ValueTask<PlayerProfile?> GetAsync(
        PlayerId id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _database.WithConnectionAsync<PlayerProfile?>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT last_known_name, first_seen_utc, last_seen_utc
                FROM ano_players
                WHERE steam_id = @steamId
                """;
            MigrationRunner.AddParameter(command, "@steamId", id.SteamId64);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return null;
            }

            return new PlayerProfile(
                id,
                reader.GetString(0),
                ToUtcOffset(reader.GetDateTime(1)),
                ToUtcOffset(reader.GetDateTime(2)));
        }, cancellationToken);
    }

    public async ValueTask UpsertAsync(
        PlayerProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ano_players (steam_id, last_known_name, first_seen_utc, last_seen_utc)
                VALUES (@steamId, @name, @firstSeen, @lastSeen)
                ON DUPLICATE KEY UPDATE
                    last_known_name = VALUES(last_known_name),
                    first_seen_utc = LEAST(first_seen_utc, VALUES(first_seen_utc)),
                    last_seen_utc = GREATEST(last_seen_utc, VALUES(last_seen_utc))
                """;
            MigrationRunner.AddParameter(command, "@steamId", profile.Id.SteamId64);
            MigrationRunner.AddParameter(command, "@name", profile.LastKnownName);
            MigrationRunner.AddParameter(command, "@firstSeen", profile.FirstSeenUtc.UtcDateTime);
            MigrationRunner.AddParameter(command, "@lastSeen", profile.LastSeenUtc.UtcDateTime);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset ToUtcOffset(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
