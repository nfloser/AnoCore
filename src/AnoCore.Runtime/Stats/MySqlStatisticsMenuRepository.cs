using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Runtime.Stats;

public sealed class MySqlStatisticsMenuRepository : IStatisticsMenuRepository
{
    private readonly IDatabase _database;
    private readonly MySqlCombatRepository _combat;
    private readonly MySqlPlaytimeRepository _playtime;

    public MySqlStatisticsMenuRepository(IDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _combat = new(database);
        _playtime = new(database);
    }

    public ValueTask<PersonalStatistics> ReadPersonalAsync(PlayerId player, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        return _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM ano_effective_combat_kills WHERE attacker_steam_id = @player),
                    (SELECT COUNT(*) FROM ano_effective_combat_deaths WHERE victim_steam_id = @player),
                    (SELECT COUNT(*) FROM ano_effective_combat_assists WHERE assister_steam_id = @player),
                    (SELECT COALESCE(SUM(TIMESTAMPDIFF(MICROSECOND, started_at_utc, accounted_until_utc)), 0)
                        FROM ano_playtime_sessions WHERE steam_id = @player),
                    (SELECT COALESCE(SUM(c.headshot), 0) FROM ano_effective_combat_kills AS k
                        JOIN ano_combat_death_context AS c ON c.event_id = k.event_id WHERE k.attacker_steam_id = @player),
                    (SELECT COUNT(*) FROM ano_effective_combat_kills AS k
                        JOIN ano_combat_death_context AS c ON c.event_id = k.event_id WHERE k.attacker_steam_id = @player),
                    (SELECT COALESCE(SUM(amount), 0) FROM ano_effective_gameplay_stats WHERE player_steam_id = @player AND stat_kind = 12),
                    (SELECT COALESCE(SUM(amount), 0) FROM ano_effective_gameplay_stats WHERE player_steam_id = @player AND stat_kind = 13),
                    (SELECT COALESCE(SUM(amount), 0) FROM ano_effective_gameplay_stats WHERE player_steam_id = @player AND stat_kind = 8),
                    (SELECT COALESCE(SUM(amount), 0) FROM ano_effective_gameplay_stats WHERE player_steam_id = @player AND stat_kind = 6)
                """;
            Add(command, "@player", player.SteamId64);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("Personal statistics query returned no row.");
            long Read(int index) => Convert.ToInt64(reader.GetValue(index), CultureInfo.InvariantCulture);
            return new PersonalStatistics(new(Read(0), Read(1), Read(2)), TimeSpan.FromTicks(checked(Read(3) * 10)),
                Read(4), Read(5), Read(6), Read(7), Read(8), Read(9));
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<StatisticsRankEntry>> GetTopAsync(StatisticsCategory category,
        int limit, int offset, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(offset));
        // Reuse existing indexed totals/ranking queries rather than introducing a second store.
        switch (category)
        {
            case StatisticsCategory.Playtime:
                return (await _playtime.GetTopAsync(limit, offset, cancellationToken).ConfigureAwait(false))
                    .Select(entry => new StatisticsRankEntry(entry.PlayerId, entry.Total.Ticks / (decimal)TimeSpan.TicksPerSecond, entry.Position, entry.DisplayName)).ToArray();
            case StatisticsCategory.Kills:
                return (await _combat.GetTopKillsAsync(limit, offset, cancellationToken).ConfigureAwait(false))
                    .Select(entry => new StatisticsRankEntry(entry.PlayerId, entry.Kills, entry.Position, entry.DisplayName)).ToArray();
            case StatisticsCategory.Deaths:
                return Counts(await _combat.GetTopDeathsAsync(limit, offset, cancellationToken).ConfigureAwait(false));
            case StatisticsCategory.Assists:
                return Counts(await _combat.GetTopAssistsAsync(limit, offset, cancellationToken).ConfigureAwait(false));
        }
        return await _database.WithConnectionAsync<IReadOnlyList<StatisticsRankEntry>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            var aggregate = category switch
            {
                StatisticsCategory.KillDeathRatio => """
                    SELECT k.attacker_steam_id AS steam_id, k.total / d.total AS value
                    FROM (SELECT attacker_steam_id, COUNT(*) AS total FROM ano_effective_combat_kills GROUP BY attacker_steam_id) AS k
                    JOIN (SELECT victim_steam_id, COUNT(*) AS total FROM ano_effective_combat_deaths GROUP BY victim_steam_id) AS d
                        ON d.victim_steam_id = k.attacker_steam_id
                    WHERE k.total >= 50 AND d.total >= 20
                    """,
                StatisticsCategory.HeadshotPercentage => """
                    SELECT k.attacker_steam_id AS steam_id, 100.0 * SUM(c.headshot) / COUNT(*) AS value
                    FROM ano_effective_combat_kills AS k JOIN ano_combat_death_context AS c ON c.event_id = k.event_id
                    GROUP BY k.attacker_steam_id HAVING COUNT(*) >= 50
                    """,
                StatisticsCategory.MatchWinPercentage => """
                    SELECT player_steam_id AS steam_id, 100.0 * SUM(CASE WHEN stat_kind = 12 THEN amount ELSE 0 END) / SUM(amount) AS value
                    FROM ano_effective_gameplay_stats WHERE stat_kind IN (12, 13)
                    GROUP BY player_steam_id HAVING SUM(amount) >= 10
                    """,
                _ => """
                    SELECT player_steam_id AS steam_id, SUM(amount) AS value
                    FROM ano_effective_gameplay_stats WHERE stat_kind = @kind
                    GROUP BY player_steam_id HAVING SUM(amount) > 0
                    """,
            };
            command.CommandText = $$"""
                SELECT ranked.steam_id, ranked.value, profiles.last_known_name
                FROM ({{aggregate}}) AS ranked
                LEFT JOIN ano_players AS profiles ON profiles.steam_id = ranked.steam_id
                ORDER BY ranked.value DESC, ranked.steam_id ASC
                LIMIT @limit OFFSET @offset
                """;
            var kind = category switch
            {
                StatisticsCategory.MatchWins => GameplayStatKind.MatchWon,
                StatisticsCategory.RoundWins => GameplayStatKind.RoundWon,
                StatisticsCategory.Mvp => GameplayStatKind.Mvp,
                StatisticsCategory.BombPlants => GameplayStatKind.BombPlanted,
                StatisticsCategory.BombDefuses => GameplayStatKind.BombDefused,
                StatisticsCategory.GrenadeKills => GameplayStatKind.GrenadeKill,
                StatisticsCategory.KnifeKills => GameplayStatKind.KnifeKill,
                _ => GameplayStatKind.MatchWon,
            };
            Add(command, "@kind", (byte)kind);
            Add(command, "@limit", limit);
            Add(command, "@offset", offset);
            var result = new List<StatisticsRankEntry>();
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                result.Add(new(new(Convert.ToUInt64(reader.GetValue(0), CultureInfo.InvariantCulture)),
                    Convert.ToDecimal(reader.GetValue(1), CultureInfo.InvariantCulture), offset + result.Count + 1,
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<StatisticsRankEntry> Counts(IReadOnlyList<CombatCountRankEntry> entries)
        => entries.Select(entry => new StatisticsRankEntry(entry.PlayerId, entry.Count, entry.Position, entry.DisplayName)).ToArray();
    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
