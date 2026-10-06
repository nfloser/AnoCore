using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression.Persistence;

public sealed partial class MySqlAchievementUnlockRepository
{
    private static async ValueTask EnsureAccountAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId playerId,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ano_progression_accounts (
                player_steam_id, lifetime_xp, revision, updated_at_utc)
            VALUES (@player, 0, 0, @updated)
            ON DUPLICATE KEY UPDATE player_steam_id = player_steam_id
            """;
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@updated", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async ValueTask<ProgressionLifetimeState> ReadLifetimeForUpdateAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId playerId,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT lifetime_xp, revision
            FROM ano_progression_accounts
            WHERE player_steam_id = @player
            FOR UPDATE
            """;
        Add(command, "@player", playerId.SteamId64);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidOperationException(
                "Progression account row disappeared during achievement unlock.");

        return new ProgressionLifetimeState(
            playerId,
            ReadNonnegativeInt64(reader.GetValue(0), "lifetime_xp"),
            ReadNonnegativeInt64(reader.GetValue(1), "revision"));
    }

    private static async ValueTask<AchievementUnlockRecord?> ReadUnlockAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId playerId,
        string achievementId,
        int tier,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT definition_version, reward_xp, grant_id, unlocked_at_utc
            FROM ano_progression_achievement_unlocks
            WHERE player_steam_id = @player
                AND achievement_id = @achievement
                AND tier = @tier
            """;
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@achievement", achievementId);
        Add(command, "@tier", tier);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            return null;

        return new AchievementUnlockRecord(
            playerId,
            achievementId,
            tier,
            Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
            ReadNonnegativeInt64(reader.GetValue(1), "reward_xp"),
            reader.GetString(2),
            Utc(reader.GetDateTime(3)));
    }

    private static async ValueTask<ProgressionGrantRecord?> ReadRewardGrantAsync(
        DbConnection connection,
        DbTransaction transaction,
        PlayerId playerId,
        string grantId,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT source, base_xp, awarded_xp, reason, occurred_at_utc,
                boost_id, boost_multiplier, lifetime_xp_after,
                account_revision_after
            FROM ano_progression_grants
            WHERE player_steam_id = @player AND grant_id = @grant
            """;
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@grant", grantId);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            return null;

        var sourceValue = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
        if (!Enum.IsDefined(typeof(ProgressionXpSource), sourceValue))
            throw new InvalidOperationException(
                "Stored achievement reward grant has an invalid XP source.");

        return new ProgressionGrantRecord(
            playerId,
            grantId,
            (ProgressionXpSource)sourceValue,
            ReadNonnegativeInt64(reader.GetValue(1), "base_xp"),
            ReadNonnegativeInt64(reader.GetValue(2), "awarded_xp"),
            reader.GetString(3),
            Utc(reader.GetDateTime(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetDecimal(6),
            ReadNonnegativeInt64(reader.GetValue(7), "lifetime_xp_after"),
            ReadNonnegativeInt64(reader.GetValue(8), "account_revision_after"));
    }
}
