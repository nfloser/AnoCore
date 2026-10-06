using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class MySqlSeasonRewardRepository : ISeasonRewardRepository
{
    private readonly IDatabase _database;
    private readonly MySqlSeasonProgressionRepository _progression;
    private readonly MySqlSeasonRepository _seasons;

    public MySqlSeasonRewardRepository(IDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _progression = new(database);
        _seasons = new(database);
    }

    public async ValueTask<int> ReconcileAsync(PersistedSeason season, int batchSize, DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(season);
        _ = SeasonCatalogSnapshot.Create([season.Definition]);
        if (batchSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(batchSize));
        cancellationToken.ThrowIfCancellationRequested();
        var stored = await _seasons.ReadAsync(season.Definition.Id, season.Definition.Version, cancellationToken)
            .ConfigureAwait(false) ?? throw new KeyNotFoundException("The season must be accepted before reward routing.");
        if (stored.Definition != season.Definition)
            throw new SeasonDefinitionConflictException(season.Definition.Id, season.Definition.Version);
        if (stored.ClosedAtUtc is not null || at < stored.Definition.StartsAtUtc) return 0;

        var pending = await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT g.player_steam_id, g.grant_id, g.source, g.base_xp, g.awarded_xp,
                    g.reason, g.occurred_at_utc, g.boost_id, g.boost_multiplier
                FROM ano_progression_grants g
                LEFT JOIN ano_progression_season_grants s
                    ON s.player_steam_id = g.player_steam_id AND s.grant_id = g.grant_id
                    AND s.season_id = @season
                WHERE s.grant_id IS NULL AND g.source IN (@gameplay, @challenge, @achievement)
                    AND g.occurred_at_utc >= @start AND g.occurred_at_utc < @end
                    AND g.occurred_at_utc <= @at
                ORDER BY g.occurred_at_utc, g.player_steam_id, g.grant_id
                LIMIT @batch
                """;
            Add(command, "@season", stored.Definition.Id);
            Add(command, "@start", stored.Definition.StartsAtUtc.UtcDateTime);
            Add(command, "@end", stored.Definition.EndsAtUtc.UtcDateTime);
            Add(command, "@at", at.UtcDateTime);
            Add(command, "@batch", batchSize);
            Add(command, "@gameplay", (int)ProgressionXpSource.Gameplay);
            Add(command, "@challenge", (int)ProgressionXpSource.ChallengeReward);
            Add(command, "@achievement", (int)ProgressionXpSource.AchievementReward);
            var result = new List<(PlayerId Player, SeasonXpGrantCandidate Candidate)>();
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var player = new PlayerId(Convert.ToUInt64(reader.GetValue(0), CultureInfo.InvariantCulture));
                var candidate = new SeasonXpGrantCandidate(stored.Definition.Id, stored.Definition.Version,
                    reader.GetString(1), (ProgressionXpSource)Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
                    Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture),
                    Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture), reader.GetString(5),
                    new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc)),
                    reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetDecimal(8));
                result.Add((player, candidate));
            }
            return result;
        }, cancellationToken).ConfigureAwait(false);

        var applied = 0;
        foreach (var (player, candidate) in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _progression.ApplyAsync(player, candidate, cancellationToken).ConfigureAwait(false);
            if (result.Applied) applied++;
        }
        return applied;
    }

    public ValueTask<IReadOnlyList<SeasonLeaderboardEntry>> ReadTopAsync(string seasonId, int seasonVersion,
        int offset, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(seasonId) || seasonId.Length > SeasonCatalogSnapshot.MaxSeasonIdLength
            || seasonId.Any(character => character > 0x7f
                || !(char.IsLetterOrDigit(character) || character is '.' or '_' or '-')) || seasonVersion < 1)
            throw new ArgumentException("Season key is invalid.", nameof(seasonId));
        if (offset is < 0 or > 100000 || limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return _database.WithConnectionAsync<IReadOnlyList<SeasonLeaderboardEntry>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT player_steam_id, season_xp
                FROM ano_progression_season_accounts
                WHERE season_id = @season AND season_version = @version
                ORDER BY season_xp DESC, player_steam_id
                LIMIT @limit OFFSET @offset
                """;
            Add(command, "@season", seasonId);
            Add(command, "@version", seasonVersion);
            Add(command, "@limit", limit);
            Add(command, "@offset", offset);
            var result = new List<SeasonLeaderboardEntry>();
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                result.Add(new(offset + result.Count + 1L,
                    new PlayerId(Convert.ToUInt64(reader.GetValue(0), CultureInfo.InvariantCulture)),
                    Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture)));
            return result.AsReadOnly();
        }, cancellationToken);
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
