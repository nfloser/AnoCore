using System.Data;
using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class MySqlAchievementRepository : IAchievementRepository
{
    private readonly IDatabase _database;

    public MySqlAchievementRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public ValueTask<int> ReadAwardedTierAsync(PlayerId playerId, string achievementId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ValidateId(achievementId);
        return _database.WithConnectionAsync((connection, token) =>
            ReadTierAsync(connection, null, playerId, achievementId, token), cancellationToken);
    }

    public ValueTask<IReadOnlyList<AchievementUnlockRecord>> UnlockAsync(PlayerId playerId,
        AchievementDefinition definition, IEnumerable<GameplayStatTotal> totals,
        DateTimeOffset occurredAt, ProgressionDefinitionSnapshot xpDefinitions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(xpDefinitions);
        var snapshot = totals.Take(Enum.GetValues<GameplayStatKind>().Length + 1).ToArray();
        _ = definition.Evaluate(snapshot, 0);
        return _database.InTransactionAsync<IReadOnlyList<AchievementUnlockRecord>>(async (connection, transaction, token) =>
        {
            await MySqlProgressionGrantRepository.EnsureAccountAsync(connection, transaction, playerId, token).ConfigureAwait(false);
            _ = await MySqlProgressionGrantRepository.ReadLifetimeForUpdateAsync(connection, transaction, playerId, token).ConfigureAwait(false);
            var awardedTier = await ReadTierAsync(connection, transaction, playerId, definition.Id, token).ConfigureAwait(false);
            foreach (var requirement in definition.Prerequisites)
            {
                var parentTier = await ReadTierAsync(connection, transaction, playerId,
                    requirement.AchievementId, token).ConfigureAwait(false);
                if (parentTier < requirement.Tier)
                    return Array.Empty<AchievementUnlockRecord>();
            }
            var evaluation = definition.Evaluate(snapshot, awardedTier);
            var unlocked = new List<AchievementUnlockRecord>();
            foreach (var tier in evaluation.NewlyUnlocked)
            {
                var grantId = "achievement:" + definition.Id + ":" + tier.Tier.ToString(CultureInfo.InvariantCulture);
                var boost = xpDefinitions.ResolveBoost(occurredAt, ProgressionXpSource.AchievementReward);
                var candidate = new ProgressionGrantCandidate(grantId, ProgressionXpSource.AchievementReward,
                    tier.RewardXp, xpDefinitions.ApplyBoost(tier.RewardXp, occurredAt, ProgressionXpSource.AchievementReward),
                    "achievement." + definition.Id, occurredAt, boost.BoostId, boost.Multiplier);
                var result = await MySqlProgressionGrantRepository.ApplyInTransactionAsync(
                    connection, transaction, playerId, candidate, token).ConfigureAwait(false);
                if (!result.Applied)
                    throw new InvalidOperationException("Achievement grant exists without its corresponding unlock.");
                await InsertUnlockAsync(connection, transaction, playerId, definition, tier.Tier, grantId, token).ConfigureAwait(false);
                unlocked.Add(new AchievementUnlockRecord(definition.Id, definition.Version, tier.Tier, result.Grant));
            }
            return unlocked.AsReadOnly();
        }, IsolationLevel.ReadCommitted, cancellationToken);
    }

    private static async ValueTask<int> ReadTierAsync(DbConnection connection, DbTransaction? transaction,
        PlayerId playerId, string id, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(MAX(tier), 0), COUNT(*), COALESCE(MIN(tier), 0) FROM ano_progression_achievements
            WHERE player_steam_id = @player AND achievement_id = @id
            """;
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@id", id);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidOperationException("Achievement aggregate query returned no row.");
        var tier = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
        var count = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
        if (tier < 0 || tier > AchievementDefinition.MaxTiers || tier != count
            || count > 0 && Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException("Stored achievement tiers are not contiguous.");
        return tier;
    }

    private static async ValueTask InsertUnlockAsync(DbConnection connection, DbTransaction transaction,
        PlayerId playerId, AchievementDefinition definition, int tier, string grantId, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_progression_achievements
                (player_steam_id, achievement_id, tier, definition_version, grant_id)
            VALUES (@player, @id, @tier, @version, @grant)
            """;
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@id", definition.Id);
        Add(command, "@tier", tier);
        Add(command, "@version", definition.Version);
        Add(command, "@grant", grantId);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64
            || id.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
            throw new ArgumentException("Achievement ID is invalid.", nameof(id));
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
