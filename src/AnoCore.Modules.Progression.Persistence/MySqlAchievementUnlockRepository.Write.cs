using System.Data.Common;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression.Persistence;

public sealed partial class MySqlAchievementUnlockRepository
{
    private static ProgressionGrantRecord CreateGrant(
        PlayerId playerId,
        AchievementTierUnlockCandidate candidate,
        long lifetimeAfter,
        long revisionAfter)
        => new(
            playerId,
            candidate.GrantId,
            ProgressionXpSource.AchievementReward,
            candidate.RewardXp,
            candidate.AwardedXp,
            $"achievement.{candidate.AchievementId}.tier.{candidate.Tier}",
            candidate.OccurredAtUtc,
            candidate.BoostId,
            candidate.BoostMultiplier,
            lifetimeAfter,
            revisionAfter);

    private static AchievementUnlockRecord CreateUnlock(
        PlayerId playerId,
        AchievementTierUnlockCandidate candidate)
        => new(
            playerId,
            candidate.AchievementId,
            candidate.Tier,
            candidate.DefinitionVersion,
            candidate.RewardXp,
            candidate.GrantId,
            candidate.OccurredAtUtc);

    private static async ValueTask InsertRewardGrantAsync(
        DbConnection connection,
        DbTransaction transaction,
        ProgressionGrantRecord grant,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_progression_grants (
                player_steam_id, grant_id, source, base_xp, awarded_xp,
                reason, occurred_at_utc, boost_id, boost_multiplier,
                lifetime_xp_after, account_revision_after, created_at_utc)
            VALUES (
                @player, @grant, @source, @base, @awarded,
                @reason, @occurred, @boost, @multiplier,
                @lifetimeAfter, @revisionAfter, @created)
            """;
        Add(command, "@player", grant.PlayerId.SteamId64);
        Add(command, "@grant", grant.GrantId);
        Add(command, "@source", (int)grant.Source);
        Add(command, "@base", grant.BaseXp);
        Add(command, "@awarded", grant.AwardedXp);
        Add(command, "@reason", grant.Reason);
        Add(command, "@occurred", grant.OccurredAtUtc.UtcDateTime);
        Add(command, "@boost", grant.BoostId is null ? DBNull.Value : grant.BoostId);
        Add(command, "@multiplier", grant.BoostMultiplier);
        Add(command, "@lifetimeAfter", grant.LifetimeXpAfter);
        Add(command, "@revisionAfter", grant.AccountRevisionAfter);
        Add(command, "@created", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async ValueTask InsertUnlockAsync(
        DbConnection connection,
        DbTransaction transaction,
        AchievementUnlockRecord unlock,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_progression_achievement_unlocks (
                player_steam_id, achievement_id, tier,
                definition_version, reward_xp, grant_id, unlocked_at_utc)
            VALUES (
                @player, @achievement, @tier,
                @version, @reward, @grant, @unlocked)
            """;
        Add(command, "@player", unlock.PlayerId.SteamId64);
        Add(command, "@achievement", unlock.AchievementId);
        Add(command, "@tier", unlock.Tier);
        Add(command, "@version", unlock.DefinitionVersion);
        Add(command, "@reward", unlock.RewardXp);
        Add(command, "@grant", unlock.GrantId);
        Add(command, "@unlocked", unlock.UnlockedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async ValueTask UpdateLifetimeAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId playerId,
        long expectedRevision,
        long lifetimeAfter,
        long revisionAfter,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ano_progression_accounts
            SET lifetime_xp = @lifetime,
                revision = @revision,
                updated_at_utc = @updated
            WHERE player_steam_id = @player AND revision = @expected
            """;
        Add(command, "@lifetime", lifetimeAfter);
        Add(command, "@revision", revisionAfter);
        Add(command, "@updated", DateTime.UtcNow);
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@expected", expectedRevision);
        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException(
                "Progression account revision changed during achievement unlock.");
    }
}
