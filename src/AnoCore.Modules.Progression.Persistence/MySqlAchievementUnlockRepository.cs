using System.Data;
using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression.Persistence;

public sealed partial class MySqlAchievementUnlockRepository : IAchievementUnlockRepository
{
    private readonly IDatabase _database;

    public MySqlAchievementUnlockRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public ValueTask<int> ReadHighestTierAsync(
        PlayerId playerId,
        string achievementId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ValidateAchievementId(achievementId);
        return _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COALESCE(MAX(tier), 0)
                FROM ano_progression_achievement_unlocks
                WHERE player_steam_id = @player
                    AND achievement_id = @achievement
                """;
            Add(command, "@player", playerId.SteamId64);
            Add(command, "@achievement", achievementId);
            var value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }, cancellationToken);
    }

    public ValueTask<AchievementTierUnlockCommit> ApplyAsync(
        PlayerId playerId,
        AchievementTierUnlockCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(candidate);
        candidate = NormalizeAndValidate(candidate);
        return ApplyCoreAsync(playerId, candidate, cancellationToken);
    }

    private ValueTask<AchievementTierUnlockCommit> ApplyCoreAsync(
        PlayerId playerId,
        AchievementTierUnlockCandidate candidate,
        CancellationToken cancellationToken)
        => _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await EnsureAccountAsync(connection, transaction, playerId, token)
                .ConfigureAwait(false);
            var lifetime = await ReadLifetimeForUpdateAsync(
                connection, transaction, playerId, token).ConfigureAwait(false);

            var existingUnlock = await ReadUnlockAsync(
                connection, transaction, playerId,
                candidate.AchievementId, candidate.Tier, token).ConfigureAwait(false);
            if (existingUnlock is not null)
            {
                var existingGrant = await ReadRewardGrantAsync(
                    connection, transaction, playerId,
                    existingUnlock.GrantId, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        "Stored achievement unlock is missing its XP reward grant.");
                ValidateStoredPair(existingUnlock, existingGrant);
                return new AchievementTierUnlockCommit(
                    false, existingUnlock, existingGrant);
            }

            if (await ReadRewardGrantAsync(
                connection, transaction, playerId, candidate.GrantId, token)
                .ConfigureAwait(false) is not null)
            {
                throw new InvalidOperationException(
                    "Achievement reward grant exists without its unlock record.");
            }

            var lifetimeAfter = checked(lifetime.LifetimeXp + candidate.AwardedXp);
            var revisionAfter = checked(lifetime.Revision + 1);
            var grant = CreateGrant(playerId, candidate, lifetimeAfter, revisionAfter);
            var unlock = CreateUnlock(playerId, candidate);

            await InsertRewardGrantAsync(connection, transaction, grant, token)
                .ConfigureAwait(false);
            await InsertUnlockAsync(connection, transaction, unlock, token)
                .ConfigureAwait(false);
            await UpdateLifetimeAsync(
                connection, transaction, playerId, lifetime.Revision,
                lifetimeAfter, revisionAfter, token).ConfigureAwait(false);

            return new AchievementTierUnlockCommit(true, unlock, grant);
        }, IsolationLevel.ReadCommitted, cancellationToken);
}
