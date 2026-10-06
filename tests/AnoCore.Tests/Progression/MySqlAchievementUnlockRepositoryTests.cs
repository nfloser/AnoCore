using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Progression;
using AnoCore.Modules.Progression.Persistence;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Tests.Progression;

[TestClass]
[DoNotParallelize]
public sealed class MySqlAchievementUnlockRepositoryTests
{
    private static readonly PlayerId Player = new(76561198000238101);
    private static readonly DateTimeOffset Friday =
        new(2026, 10, 9, 18, 0, 0, TimeSpan.Zero);
    private MySqlDatabase _database = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection))
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured for integration tests.");

        _database = new MySqlDatabase(connection!);
        await DropAsync();
        await ProgressionPersistenceBootstrap.EnsureReadyAsync(_database);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    [TestMethod]
    public async Task Unlock_CommitsPermanentTierAndLifetimeRewardTogether()
    {
        var service = Service();
        var definition = Definition(1, 100);

        var result = await service.EvaluateAndCommitAsync(
            Player,
            definition,
            [new(GameplayStatKind.HeadshotKill, 10)],
            Friday);

        var commit = result.Unlocks.Single();
        var lifetime = await new MySqlProgressionGrantRepository(_database)
            .ReadLifetimeAsync(Player);

        Assert.IsTrue(commit.Applied);
        Assert.AreEqual(1, commit.Unlock.Tier);
        Assert.AreEqual(1, commit.Unlock.DefinitionVersion);
        Assert.AreEqual(100L, commit.Unlock.RewardXp);
        Assert.AreEqual(ProgressionXpSource.AchievementReward, commit.RewardGrant.Source);
        Assert.AreEqual(100L, commit.RewardGrant.AwardedXp);
        Assert.AreEqual(100L, lifetime.LifetimeXp);
        Assert.AreEqual(1L, lifetime.Revision);
    }

    [TestMethod]
    public async Task GameplayBoost_DoesNotMultiplyAchievementRewardByDefault()
    {
        var service = Service(
            new XpBoostDefinition(
                "double-gameplay",
                Friday.AddHours(-1),
                Friday.AddHours(1),
                2m));

        var result = await service.EvaluateAndCommitAsync(
            Player,
            Definition(1, 125),
            [new(GameplayStatKind.HeadshotKill, 10)],
            Friday);

        var grant = result.Unlocks.Single().RewardGrant;
        Assert.AreEqual(125L, grant.BaseXp);
        Assert.AreEqual(125L, grant.AwardedXp);
        Assert.AreEqual(1m, grant.BoostMultiplier);
        Assert.IsNull(grant.BoostId);
    }

    [TestMethod]
    public async Task ExplicitAchievementBoost_IsPersistedWithOriginalReward()
    {
        var service = Service(
            new XpBoostDefinition(
                "achievement-weekend",
                Friday.AddHours(-1),
                Friday.AddHours(1),
                2m,
                ProgressionXpSourceMask.AchievementReward));

        var result = await service.EvaluateAndCommitAsync(
            Player,
            Definition(3, 80),
            [new(GameplayStatKind.HeadshotKill, 10)],
            Friday);

        var grant = result.Unlocks.Single().RewardGrant;
        Assert.AreEqual(80L, grant.BaseXp);
        Assert.AreEqual(160L, grant.AwardedXp);
        Assert.AreEqual("achievement-weekend", grant.BoostId);
        Assert.AreEqual(2m, grant.BoostMultiplier);
    }

    [TestMethod]
    public async Task RetryAfterRestart_PreservesFirstDefinitionAndReward()
    {
        var first = Service();
        await first.EvaluateAndCommitAsync(
            Player,
            Definition(1, 100),
            [new(GameplayStatKind.HeadshotKill, 10)],
            Friday);

        var repository = new MySqlAchievementUnlockRepository(_database);
        var retry = await repository.ApplyAsync(
            Player,
            new AchievementTierUnlockCandidate(
                "headshots",
                1,
                2,
                999,
                AchievementUnlockService.GrantId("headshots", 1),
                Friday.AddDays(1),
                999,
                null,
                1m));

        var lifetime = await new MySqlProgressionGrantRepository(_database)
            .ReadLifetimeAsync(Player);

        Assert.IsFalse(retry.Applied);
        Assert.AreEqual(1, retry.Unlock.DefinitionVersion);
        Assert.AreEqual(100L, retry.Unlock.RewardXp);
        Assert.AreEqual(100L, retry.RewardGrant.BaseXp);
        Assert.AreEqual(100L, retry.RewardGrant.AwardedXp);
        Assert.AreEqual(100L, lifetime.LifetimeXp);
        Assert.AreEqual(1L, lifetime.Revision);
    }

    [TestMethod]
    public async Task ConcurrentEvaluators_AwardTierExactlyOnce()
    {
        var definition = Definition(1, 100);
        var first = Service();
        var second = Service();

        var results = await Task.WhenAll(
            first.EvaluateAndCommitAsync(
                Player,
                definition,
                [new(GameplayStatKind.HeadshotKill, 10)],
                Friday).AsTask(),
            second.EvaluateAndCommitAsync(
                Player,
                definition,
                [new(GameplayStatKind.HeadshotKill, 10)],
                Friday).AsTask());

        Assert.AreEqual(
            1,
            results.SelectMany(value => value.Unlocks)
                .Count(value => value.Applied));
        var lifetime = await new MySqlProgressionGrantRepository(_database)
            .ReadLifetimeAsync(Player);
        Assert.AreEqual(100L, lifetime.LifetimeXp);
        Assert.AreEqual(1L, lifetime.Revision);
        Assert.AreEqual(
            1,
            await new MySqlAchievementUnlockRepository(_database)
                .ReadHighestTierAsync(Player, "headshots"));
    }

    [TestMethod]
    public async Task UnlockInsertFailure_RollsBackRewardAndLifetime()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER fail_achievement_unlock
                BEFORE INSERT ON ano_progression_achievement_unlocks
                FOR EACH ROW
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'forced achievement unlock failure'
                """;
            await command.ExecuteNonQueryAsync(token);
            return true;
        });

        await Assert.ThrowsExactlyAsync<MySqlConnector.MySqlException>(async () =>
            await Service().EvaluateAndCommitAsync(
                Player,
                Definition(1, 100),
                [new(GameplayStatKind.HeadshotKill, 10)],
                Friday));

        var lifetime = await new MySqlProgressionGrantRepository(_database)
            .ReadLifetimeAsync(Player);
        var grant = await new MySqlProgressionGrantRepository(_database)
            .ReadGrantAsync(
                Player,
                AchievementUnlockService.GrantId("headshots", 1));

        Assert.AreEqual(0L, lifetime.LifetimeXp);
        Assert.AreEqual(0L, lifetime.Revision);
        Assert.IsNull(grant);
        Assert.AreEqual(
            0,
            await new MySqlAchievementUnlockRepository(_database)
                .ReadHighestTierAsync(Player, "headshots"));
    }

    [TestMethod]
    public async Task LifetimeOverflow_RollsBackUnlockAndReward()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ano_progression_accounts (
                    player_steam_id, lifetime_xp, revision, updated_at_utc)
                VALUES (@player, @xp, 1, @updated)
                """;
            var player = command.CreateParameter();
            player.ParameterName = "@player";
            player.Value = Player.SteamId64;
            command.Parameters.Add(player);
            var xp = command.CreateParameter();
            xp.ParameterName = "@xp";
            xp.Value = long.MaxValue;
            command.Parameters.Add(xp);
            var updated = command.CreateParameter();
            updated.ParameterName = "@updated";
            updated.Value = DateTime.UtcNow;
            command.Parameters.Add(updated);
            await command.ExecuteNonQueryAsync(token);
            return true;
        });

        await Assert.ThrowsExactlyAsync<OverflowException>(async () =>
            await Service().EvaluateAndCommitAsync(
                Player,
                Definition(1, 1),
                [new(GameplayStatKind.HeadshotKill, 10)],
                Friday));

        Assert.AreEqual(
            0,
            await new MySqlAchievementUnlockRepository(_database)
                .ReadHighestTierAsync(Player, "headshots"));
        Assert.IsNull(await new MySqlProgressionGrantRepository(_database)
            .ReadGrantAsync(
                Player,
                AchievementUnlockService.GrantId("headshots", 1)));
    }

    private AchievementUnlockService Service(params XpBoostDefinition[] boosts)
        => new(
            new MySqlAchievementUnlockRepository(_database),
            ProgressionDefinitionSnapshot.Create(
                [new(1, 0), new(2, 100)],
                boosts));

    private static AchievementDefinition Definition(int version, long reward)
        => AchievementDefinition.Create(
            "headshots",
            version,
            GameplayStatKind.HeadshotKill,
            [new AchievementTier(1, 10, reward)]);

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var statement in new[]
            {
                "DROP TRIGGER IF EXISTS fail_achievement_unlock",
                "DROP TABLE IF EXISTS ano_progression_achievement_unlocks",
                "DROP TABLE IF EXISTS ano_progression_season_grants",
                "DROP TABLE IF EXISTS ano_progression_season_accounts",
                "DROP TABLE IF EXISTS ano_progression_season_runtime",
                "DROP TABLE IF EXISTS ano_progression_seasons",
                "DROP TABLE IF EXISTS ano_progression_grants",
                "DROP TABLE IF EXISTS ano_progression_accounts",
                "DROP TABLE IF EXISTS ano_schema_migrations",
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = statement;
                await command.ExecuteNonQueryAsync(token);
            }

            return true;
        });
    }
}
