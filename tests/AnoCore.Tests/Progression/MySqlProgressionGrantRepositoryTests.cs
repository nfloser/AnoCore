using AnoCore.Abstractions.Players;
using AnoCore.Modules.Progression;
using AnoCore.Modules.Progression.Persistence;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Tests.Progression;

[TestClass]
[DoNotParallelize]
public sealed class MySqlProgressionGrantRepositoryTests
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
    public async Task Grant_RetryAndRestart_AreDurableAndIdempotent()
    {
        var firstService = Service(new MySqlProgressionGrantRepository(_database));
        var first = await firstService.GrantAsync(Player,
            new("round:1", ProgressionXpSource.Gameplay, 60, "gameplay.round", Friday));

        var restartedRepository = new MySqlProgressionGrantRepository(_database);
        var restartedService = Service(restartedRepository);
        var retry = await restartedService.GrantAsync(Player,
            new("round:1", ProgressionXpSource.Gameplay, 60, "gameplay.round",
                Friday.ToOffset(TimeSpan.FromHours(2))));
        var lifetime = await restartedRepository.ReadLifetimeAsync(Player);

        Assert.IsTrue(first.Applied);
        Assert.IsFalse(retry.Applied);
        Assert.AreEqual(120L, lifetime.LifetimeXp);
        Assert.AreEqual(1L, lifetime.Revision);
        Assert.AreEqual(first.Grant, retry.Grant);
    }

    [TestMethod]
    public async Task Grant_PreservesFullDecimalBoostMetadataAfterRestart()
    {
        var repository = new MySqlProgressionGrantRepository(_database);
        var committed = await repository.ApplyAsync(Player,
            new ProgressionGrantCandidate(
                "precision", ProgressionXpSource.Gameplay, 1000, 1234,
                "gameplay.round", Friday, "fractional", 1.2345m));

        var restarted = new MySqlProgressionGrantRepository(_database);
        var stored = await restarted.ReadGrantAsync(Player, "precision");

        Assert.IsTrue(committed.Applied);
        Assert.IsNotNull(stored);
        Assert.AreEqual(1.2345m, stored.BoostMultiplier);
        Assert.AreEqual(1234L, stored.AwardedXp);
    }

    [TestMethod]
    public async Task SameOriginalGrant_FirstCommittedBoostWinsAcrossDefinitionChanges()
    {
        var repository = new MySqlProgressionGrantRepository(_database);
        var occurred = Friday;
        var first = await repository.ApplyAsync(Player,
            new ProgressionGrantCandidate(
                "race", ProgressionXpSource.Gameplay, 10, 20,
                "gameplay.round", occurred, "double", 2m));
        var retry = await repository.ApplyAsync(Player,
            new ProgressionGrantCandidate(
                "race", ProgressionXpSource.Gameplay, 10, 30,
                "gameplay.round", occurred, "triple", 3m));

        Assert.IsTrue(first.Applied);
        Assert.IsFalse(retry.Applied);
        Assert.AreEqual(first.Grant, retry.Grant);
        Assert.AreEqual(20L, (await repository.ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task ConflictingDuplicate_DoesNotChangeLifetime()
    {
        var service = Service(new MySqlProgressionGrantRepository(_database));
        await service.GrantAsync(Player,
            new("event", ProgressionXpSource.Gameplay, 10, "gameplay.kill", Friday));

        await Assert.ThrowsExactlyAsync<ProgressionGrantConflictException>(async () =>
            await service.GrantAsync(Player,
                new("event", ProgressionXpSource.Gameplay, 11, "gameplay.kill", Friday)));

        var state = await service.ReadLifetimeAsync(Player);
        Assert.AreEqual(20L, state.LifetimeXp);
        Assert.AreEqual(1L, state.Revision);
    }

    [TestMethod]
    public async Task ConcurrentSameGrant_AppliesExactlyOnce()
    {
        var service = Service(new MySqlProgressionGrantRepository(_database));
        var request = new ProgressionGrantRequest(
            "same", ProgressionXpSource.Gameplay, 25, "gameplay.round", Friday);

        var results = await Task.WhenAll(
            service.GrantAsync(Player, request).AsTask(),
            service.GrantAsync(Player, request).AsTask());

        Assert.AreEqual(1, results.Count(result => result.Applied));
        Assert.AreEqual(1, results.Count(result => !result.Applied));
        var state = await service.ReadLifetimeAsync(Player);
        Assert.AreEqual(50L, state.LifetimeXp);
        Assert.AreEqual(1L, state.Revision);
    }

    [TestMethod]
    public async Task ConcurrentDifferentGrants_SerializeAccountTotals()
    {
        var service = Service(new MySqlProgressionGrantRepository(_database));

        var results = await Task.WhenAll(
            service.GrantAsync(Player,
                new("a", ProgressionXpSource.Gameplay, 10, "gameplay.round", Friday)).AsTask(),
            service.GrantAsync(Player,
                new("b", ProgressionXpSource.Gameplay, 15, "gameplay.round", Friday)).AsTask());

        Assert.AreEqual(2, results.Count(result => result.Applied));
        var state = await service.ReadLifetimeAsync(Player);
        Assert.AreEqual(50L, state.LifetimeXp);
        Assert.AreEqual(2L, state.Revision);
        CollectionAssert.AreEqual(
            new[] { 1L, 2L },
            results.Select(result => result.Grant.AccountRevisionAfter)
                .OrderBy(value => value).ToArray());
    }

    [TestMethod]
    public async Task FailedLedgerWrite_RollsBackAccountMutation()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER fail_progression_grant
                BEFORE INSERT ON ano_progression_grants
                FOR EACH ROW
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'forced progression failure'
                """;
            await command.ExecuteNonQueryAsync(token);
            return true;
        });

        var service = Service(new MySqlProgressionGrantRepository(_database));
        await Assert.ThrowsExactlyAsync<MySqlConnector.MySqlException>(async () =>
            await service.GrantAsync(Player,
                new("broken", ProgressionXpSource.Gameplay, 10, "gameplay.round", Friday)));

        var state = await service.ReadLifetimeAsync(Player);
        Assert.AreEqual(0L, state.LifetimeXp);
        Assert.AreEqual(0L, state.Revision);
    }

    [TestMethod]
    public async Task LifetimeOverflow_RollsBackGrant()
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

        var service = Service(new MySqlProgressionGrantRepository(_database));
        await Assert.ThrowsExactlyAsync<OverflowException>(async () =>
            await service.GrantAsync(Player,
                new("overflow", ProgressionXpSource.ChallengeReward,
                    1, "challenge.reward", Friday)));

        var state = await service.ReadLifetimeAsync(Player);
        Assert.AreEqual(long.MaxValue, state.LifetimeXp);
        Assert.AreEqual(1L, state.Revision);
        var stored = await new MySqlProgressionGrantRepository(_database)
            .ReadGrantAsync(Player, "overflow");
        Assert.IsNull(stored);
    }

    [TestMethod]
    public async Task Bootstrap_IsIdempotent()
    {
        await ProgressionPersistenceBootstrap.EnsureReadyAsync(_database);
        await ProgressionPersistenceBootstrap.EnsureReadyAsync(_database);

        var repository = new MySqlProgressionGrantRepository(_database);
        var state = await repository.ReadLifetimeAsync(Player);
        Assert.AreEqual(0L, state.LifetimeXp);
    }

    private static ProgressionGrantService Service(IProgressionGrantRepository repository)
        => new(repository, ProgressionDefinitionSnapshot.Create(
            [new(1, 0), new(2, 100)],
            [new("double", Friday, Friday.AddDays(2), 2m)]));

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var statement in new[]
            {
                "DROP TRIGGER IF EXISTS fail_progression_grant",
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
