using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Stats;

public sealed class MySqlCombatRepository : ICombatRepository
{
    private readonly IDatabase _database;

    public MySqlCombatRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public async ValueTask RecordAsync(CombatDeath death, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(death);
        await _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO ano_combat_deaths (
                    event_id, victim_steam_id, attacker_steam_id, assister_steam_id,
                    occurred_at_utc, is_team_kill)
                VALUES (@id, @victim, @attacker, @assister, @occurred, @team)
                ON DUPLICATE KEY UPDATE event_id = event_id
                """;
            Add(insert, "@id", death.EventId.ToString("D"));
            Add(insert, "@victim", death.VictimId.SteamId64);
            Add(insert, "@attacker", Steam(death.AttackerId));
            Add(insert, "@assister", Steam(death.AssisterId));
            Add(insert, "@occurred", death.OccurredAtUtc.UtcDateTime);
            Add(insert, "@team", death.IsTeamKill);
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);

            await using var verify = connection.CreateCommand();
            verify.Transaction = transaction;
            verify.CommandText = """
                SELECT victim_steam_id, attacker_steam_id, assister_steam_id,
                    occurred_at_utc, is_team_kill
                FROM ano_combat_deaths WHERE event_id = @id FOR UPDATE
                """;
            Add(verify, "@id", death.EventId.ToString("D"));
            await using var reader = await verify.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)
                || ReadPlayer(reader, 0) != death.VictimId
                || ReadPlayer(reader, 1) != death.AttackerId
                || ReadPlayer(reader, 2) != death.AssisterId
                || reader.GetBoolean(4) != death.IsTeamKill)
            {
                throw new InvalidOperationException("Combat event id conflicts with a different death.");
            }

            return true;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<CombatTotals> ReadAsync(PlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        return _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM ano_combat_deaths
                     WHERE attacker_steam_id = @player AND is_team_kill = 0),
                    (SELECT COUNT(*) FROM ano_combat_deaths
                     WHERE victim_steam_id = @player),
                    (SELECT COUNT(*) FROM ano_combat_deaths
                     WHERE assister_steam_id = @player)
                """;
            Add(command, "@player", playerId.SteamId64);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                throw new InvalidOperationException("Combat aggregate query returned no row.");
            return new CombatTotals(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
        }, cancellationToken);
    }

    public ValueTask<IReadOnlyList<CombatRankEntry>> GetTopKillsAsync(int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(offset));
        return _database.WithConnectionAsync<IReadOnlyList<CombatRankEntry>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT ranked.attacker_steam_id, ranked.kills, profiles.last_known_name
                FROM (
                    SELECT attacker_steam_id, COUNT(*) AS kills
                    FROM ano_combat_deaths
                    WHERE attacker_steam_id IS NOT NULL AND is_team_kill = 0
                    GROUP BY attacker_steam_id
                ) AS ranked
                LEFT JOIN ano_players AS profiles ON profiles.steam_id = ranked.attacker_steam_id
                ORDER BY ranked.kills DESC, ranked.attacker_steam_id ASC
                LIMIT @limit OFFSET @offset
                """;
            Add(command, "@limit", limit);
            Add(command, "@offset", offset);
            var entries = new List<CombatRankEntry>();
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                entries.Add(new CombatRankEntry(
                    new PlayerId(Convert.ToUInt64(reader.GetValue(0), CultureInfo.InvariantCulture)),
                    reader.GetInt64(1), offset + entries.Count + 1,
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            return entries;
        }, cancellationToken);
    }

    private static PlayerId? ReadPlayer(DbDataReader reader, int index)
        => reader.IsDBNull(index) ? null
            : new PlayerId(Convert.ToUInt64(reader.GetValue(index), CultureInfo.InvariantCulture));
    private static object Steam(PlayerId? id) => id is null ? DBNull.Value : id.SteamId64;
    private static void Add(DbCommand command, string name, object value)
        => MigrationRunner.AddParameter(command, name, value);
}
