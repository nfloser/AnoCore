using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Progression;
using AnoCore.Modules.Progression.Persistence;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Tests.Progression;

[TestClass]
[DoNotParallelize]
public sealed class MySqlAchievementRepositoryTests
{
    private static readonly PlayerId Player = new(76561198000248101);
    private static readonly DateTimeOffset Friday = new(2026, 10, 9, 18, 0, 0, TimeSpan.Zero);
    private MySqlDatabase _database = null!;
    private static ProgressionDefinitionSnapshot Xp => ProgressionDefinitionSnapshot.Create(
        [new(1, 0)], [new("weekend", Friday, Friday.AddDays(2), 2m)]);
    private static AchievementDefinition Achievement(long reward = 100) => AchievementDefinition.Create(
        "headshots", 1, GameplayStatKind.HeadshotKill, [new(1, 10, reward), new(2, 50, 200)]);

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured.");
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
    public async Task Unlock_CommitsAllTiersAndPreservesRewardsAcrossRestartAndDefinitionChanges()
    {
        var repository = new MySqlAchievementRepository(_database);
        var result = await repository.UnlockAsync(Player, Achievement(), [new(GameplayStatKind.HeadshotKill, 50)], Friday, Xp);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(ProgressionXpSource.AchievementReward, result[0].Grant.Source);
        Assert.AreEqual(100L, result[0].Grant.AwardedXp);
        Assert.AreEqual(300L, result[1].Grant.LifetimeXpAfter);
        var restarted = new MySqlAchievementRepository(_database);
        var retry = await restarted.UnlockAsync(Player, Achievement(999), [new(GameplayStatKind.HeadshotKill, 50)], Friday.AddDays(7), Xp);
        Assert.IsEmpty(retry);
        Assert.AreEqual(2, await restarted.ReadAwardedTierAsync(Player, "headshots"));
        Assert.AreEqual(300L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task ConcurrentUnlocks_AwardEachTierExactlyOnce()
    {
        var tasks = Enumerable.Range(0, 8).Select(_ => new MySqlAchievementRepository(_database)
            .UnlockAsync(Player, Achievement(), [new(GameplayStatKind.HeadshotKill, 50)], Friday, Xp).AsTask()).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.AreEqual(2, results.Sum(result => result.Count));
        var state = await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player);
        Assert.AreEqual(300L, state.LifetimeXp);
        Assert.AreEqual(2L, state.Revision);
    }

    [TestMethod]
    public async Task StatisticsReset_DoesNotRemovePermanentUnlocks()
    {
        var repository = new MySqlAchievementRepository(_database);
        await repository.UnlockAsync(Player, Achievement(), [new(GameplayStatKind.HeadshotKill, 10)], Friday, Xp);
        Assert.IsEmpty(await repository.UnlockAsync(Player, Achievement(), [], Friday, Xp));
        Assert.AreEqual(1, await repository.ReadAwardedTierAsync(Player, "headshots"));
    }

    [TestMethod]
    public async Task LaterTierFailure_RollsBackEarlierTierAndXpInSameBatch()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER fail_achievement_unlock BEFORE INSERT ON ano_progression_achievements
                FOR EACH ROW BEGIN
                    IF NEW.tier = 2 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'test failure'; END IF;
                END
                """;
            await command.ExecuteNonQueryAsync(token);
            return true;
        });
        var repository = new MySqlAchievementRepository(_database);
        await Assert.ThrowsAsync<Exception>(async () =>
            await repository.UnlockAsync(Player, Achievement(), [new(GameplayStatKind.HeadshotKill, 50)], Friday, Xp));
        Assert.AreEqual(0, await repository.ReadAwardedTierAsync(Player, "headshots"));
        var state = await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player);
        Assert.AreEqual(0L, state.LifetimeXp);
        Assert.AreEqual(0L, state.Revision);
        Assert.IsNull(await new MySqlProgressionGrantRepository(_database).ReadGrantAsync(Player, "achievement:headshots:1"));
    }

    [TestMethod]
    public async Task Overflow_RejectsUnlockAndGrantTogether()
    {
        var grants = new MySqlProgressionGrantRepository(_database);
        await grants.ApplyAsync(Player, new("seed", ProgressionXpSource.Administration,
            long.MaxValue, long.MaxValue, "test.seed", Friday, null, 1m));
        var repository = new MySqlAchievementRepository(_database);
        await Assert.ThrowsExactlyAsync<OverflowException>(async () =>
            await repository.UnlockAsync(Player, Achievement(), [new(GameplayStatKind.HeadshotKill, 10)], Friday, Xp));
        Assert.AreEqual(0, await repository.ReadAwardedTierAsync(Player, "headshots"));
        Assert.AreEqual(long.MaxValue, (await grants.ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task ExplicitAchievementBoostOptIn_IsRecorded()
    {
        var definitions = ProgressionDefinitionSnapshot.Create([new(1, 0)],
            [new("achievement-event", Friday, Friday.AddDays(2), 3m, ProgressionXpSourceMask.AchievementReward)]);
        var result = await new MySqlAchievementRepository(_database).UnlockAsync(Player,
            Achievement(), [new(GameplayStatKind.HeadshotKill, 10)], Friday, definitions);
        Assert.AreEqual(300L, result.Single().Grant.AwardedXp);
        Assert.AreEqual("achievement-event", result.Single().Grant.BoostId);
    }

    [TestMethod]
    public async Task OrphanGrantCollision_DoesNotCreateAnUnlockOrChangeXp()
    {
        var grants = new MySqlProgressionGrantRepository(_database);
        await grants.ApplyAsync(Player, new("achievement:headshots:1", ProgressionXpSource.AchievementReward,
            100, 100, "achievement.headshots", Friday, null, 1m));
        var repository = new MySqlAchievementRepository(_database);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await repository.UnlockAsync(Player, Achievement(), [new(GameplayStatKind.HeadshotKill, 10)], Friday, Xp));
        Assert.AreEqual(0, await repository.ReadAwardedTierAsync(Player, "headshots"));
        Assert.AreEqual(100L, (await grants.ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task UnlockIdentity_IsIndependentForDifferentPlayers()
    {
        var other = new PlayerId(Player.SteamId64 + 1);
        var repository = new MySqlAchievementRepository(_database);
        await repository.UnlockAsync(Player, Achievement(), [new(GameplayStatKind.HeadshotKill, 10)], Friday, Xp);
        var result = await repository.UnlockAsync(other, Achievement(), [new(GameplayStatKind.HeadshotKill, 10)], Friday, Xp);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(other, result.Single().Grant.PlayerId);
        Assert.AreEqual(100L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(other)).LifetimeXp);
    }

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var sql in new[]
            {
                "DROP TRIGGER IF EXISTS fail_achievement_unlock",
                "DROP TABLE IF EXISTS ano_progression_challenges",
                "DROP TABLE IF EXISTS ano_progression_achievements",
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
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(token);
            }
            return true;
        });
    }
}
