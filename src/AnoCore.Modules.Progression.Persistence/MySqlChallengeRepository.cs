using System.Data;
using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class MySqlChallengeRepository : IChallengeRepository
{
    private readonly IDatabase _database;

    public MySqlChallengeRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public ValueTask<ChallengeEvaluation> ReadAsync(PlayerId playerId, ChallengeCatalogSnapshot catalog,
        string challengeId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var definition = Validate(playerId, catalog, challengeId);
        at = NormalizeUtc(at);
        return _database.WithConnectionAsync(async (connection, token) =>
        {
            var state = await EvaluateAsync(connection, null, playerId, catalog, definition, at, token).ConfigureAwait(false);
            return state.Evaluation;
        }, cancellationToken);
    }

    public ValueTask<ChallengeCompletionResult> CompleteAsync(PlayerId playerId, ChallengeCatalogSnapshot catalog,
        string challengeId, DateTimeOffset at, ProgressionDefinitionSnapshot xpDefinitions,
        CancellationToken cancellationToken = default)
    {
        var definition = Validate(playerId, catalog, challengeId);
        ArgumentNullException.ThrowIfNull(xpDefinitions);
        at = NormalizeUtc(at);
        return _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            // All completions/rewards for one player share the lifetime account lock.
            await MySqlProgressionGrantRepository.EnsureAccountAsync(connection, transaction, playerId, token).ConfigureAwait(false);
            _ = await MySqlProgressionGrantRepository.ReadLifetimeForUpdateAsync(connection, transaction, playerId, token).ConfigureAwait(false);
            var state = await EvaluateAsync(connection, transaction, playerId, catalog, definition, at, token).ConfigureAwait(false);
            if (!state.Evaluation.CompletionCandidate)
                return state;

            var grantId = "challenge:" + definition.Id + ":" + definition.StartsAtUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            var boost = xpDefinitions.ResolveBoost(at, ProgressionXpSource.ChallengeReward);
            var candidate = new ProgressionGrantCandidate(grantId, ProgressionXpSource.ChallengeReward,
                definition.RewardXp, xpDefinitions.ApplyBoost(definition.RewardXp, at, ProgressionXpSource.ChallengeReward),
                "challenge." + definition.Id, at, boost.BoostId, boost.Multiplier);
            var grant = await MySqlProgressionGrantRepository.ApplyInTransactionAsync(
                connection, transaction, playerId, candidate, token).ConfigureAwait(false);
            if (!grant.Applied)
                throw new InvalidOperationException("Challenge grant exists without its corresponding completion.");

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO ano_progression_challenges
                    (player_steam_id, challenge_id, starts_at_utc, ends_at_utc, definition_version, grant_id)
                VALUES (@player, @id, @start, @end, @version, @grant)
                """;
            Add(insert, "@player", playerId.SteamId64);
            Add(insert, "@id", definition.Id);
            Add(insert, "@start", definition.StartsAtUtc.UtcDateTime);
            Add(insert, "@end", definition.EndsAtUtc.UtcDateTime);
            Add(insert, "@version", definition.Version);
            Add(insert, "@grant", grantId);
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return new ChallengeCompletionResult(true,
                state.Evaluation with { State = ChallengeEvaluationState.Completed, CompletionCandidate = false },
                new ChallengeCompletionRecord(definition.Id, definition.Version, definition.StartsAtUtc, definition.EndsAtUtc, grant.Grant));
        }, IsolationLevel.ReadCommitted, cancellationToken);
    }

    private static async ValueTask<ChallengeCompletionResult> EvaluateAsync(DbConnection connection,
        DbTransaction? transaction, PlayerId playerId, ChallengeCatalogSnapshot catalog,
        ChallengeDefinition definition, DateTimeOffset at, CancellationToken token)
    {
        var existing = await ReadCompletionAsync(connection, transaction, playerId, definition, token).ConfigureAwait(false);
        if (existing is not null)
            return new ChallengeCompletionResult(false,
                catalog.Evaluate(definition.Id, [new(definition.Statistic, definition.Target)], [definition.Id], at), existing);

        var completed = new List<string>();
        foreach (var prerequisiteId in definition.PrerequisiteIds)
        {
            var prerequisite = catalog.Get(prerequisiteId);
            if (await ReadCompletionAsync(connection, transaction, playerId, prerequisite, token).ConfigureAwait(false) is not null)
                completed.Add(prerequisiteId);
        }

        long progress = 0;
        if (at >= definition.StartsAtUtc)
        {
            await using var count = connection.CreateCommand();
            count.Transaction = transaction;
            // Raw durable events intentionally preserve challenge progress across statistics resets.
            count.CommandText = """
                SELECT COALESCE(SUM(amount), 0) FROM ano_gameplay_stats
                WHERE player_steam_id = @player AND stat_kind = @kind
                    AND occurred_at_utc >= @start AND occurred_at_utc < @end
                    AND occurred_at_utc <= @at
                """;
            Add(count, "@player", playerId.SteamId64);
            Add(count, "@kind", (byte)definition.Statistic);
            Add(count, "@start", definition.StartsAtUtc.UtcDateTime);
            Add(count, "@end", definition.EndsAtUtc.UtcDateTime);
            Add(count, "@at", at.UtcDateTime);
            progress = Convert.ToInt64(await count.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        return new ChallengeCompletionResult(false,
            catalog.Evaluate(definition.Id, [new(definition.Statistic, progress)], completed, at), null);
    }

    private static async ValueTask<ChallengeCompletionRecord?> ReadCompletionAsync(DbConnection connection,
        DbTransaction? transaction, PlayerId playerId, ChallengeDefinition definition, CancellationToken token)
    {
        int version;
        string grantId;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT ends_at_utc, definition_version, grant_id FROM ano_progression_challenges
                WHERE player_steam_id = @player AND challenge_id = @id AND starts_at_utc = @start
                """;
            Add(command, "@player", playerId.SteamId64);
            Add(command, "@id", definition.Id);
            Add(command, "@start", definition.StartsAtUtc.UtcDateTime);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
            var end = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc));
            if (end != definition.EndsAtUtc)
                throw new InvalidOperationException("A completed challenge occurrence cannot change its end boundary.");
            version = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
            grantId = reader.GetString(2);
        }
        var grant = await MySqlProgressionGrantRepository.ReadGrantAsync(
            connection, transaction, playerId, grantId, false, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Stored challenge completion has no XP grant.");
        return new ChallengeCompletionRecord(definition.Id, version, definition.StartsAtUtc, definition.EndsAtUtc, grant);
    }

    private static ChallengeDefinition Validate(PlayerId playerId, ChallengeCatalogSnapshot catalog, string challengeId)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(catalog);
        var definition = catalog.Get(challengeId);
        foreach (var item in catalog.Challenges)
        {
            if (item.StartsAtUtc.Ticks % TimeSpan.TicksPerMicrosecond != 0
                || item.EndsAtUtc.Ticks % TimeSpan.TicksPerMicrosecond != 0)
                throw new ArgumentException("Persisted challenge windows require microsecond precision.", nameof(catalog));
        }
        return definition;
    }

    private static DateTimeOffset NormalizeUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
