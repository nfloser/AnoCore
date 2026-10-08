using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class StatisticsLeaderboardIndexMigration021 : IDatabaseMigration
{
    public long Version => 21;
    public string Name => "statistics-leaderboard-index";

    public async ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var columns = new List<string>();
        await using (var inspect = connection.CreateCommand())
        {
            inspect.CommandText = """
                SELECT column_name FROM information_schema.statistics
                WHERE table_schema = DATABASE() AND table_name = 'ano_gameplay_stats'
                    AND index_name = 'ix_ano_gameplay_stats_kind_player_time'
                ORDER BY seq_in_index
                """;
            await using var reader = await inspect.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) columns.Add(reader.GetString(0));
        }
        if (columns.Count > 0)
        {
            if (!columns.SequenceEqual(new[] { "stat_kind", "player_steam_id", "occurred_at_utc", "amount" }, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Statistics leaderboard index has an unexpected definition.");
            return;
        }
        await using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE INDEX ix_ano_gameplay_stats_kind_player_time
            ON ano_gameplay_stats (stat_kind, player_steam_id, occurred_at_utc, amount)
            """;
        await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
