using AnoCore.Abstractions.Players;
using AnoCore.Modules.Progression;
using AnoCore.Modules.Progression.Persistence;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Tests.Progression;

[TestClass]
[DoNotParallelize]
public sealed class MySqlSeasonRewardRepositoryTests
{
    private static readonly PlayerId Player = new(76561198000238101);
    private static readonly DateTimeOffset Start =
        new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End =
        new(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);
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
        await new MySqlSeasonRepository(_database).AcceptAsync(
            new SeasonDefinition("s1", 1, "Season One", Start, End),
            Start.AddDays(-7));
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    private MySqlSeasonRewardRepository Repository => new(_database);
    private async Task<PersistedSeason> Season()
        => (await new MySqlSeasonRepository(_database).ReadAsync("s1", 1))!;

    [TestMethod]
    public async Task Routing_PreservesCommittedAmountsAndOnlyEligibleSourcesTimes()
    {
        await Grant("gameplay", ProgressionXpSource.Gameplay, Start, 10, 2);
        await Grant("challenge", ProgressionXpSource.ChallengeReward, Start, 20);
        await Grant("achievement", ProgressionXpSource.AchievementReward, Start, 30);
        await Grant("admin", ProgressionXpSource.Administration, Start, 100);
        await Grant("before", ProgressionXpSource.Gameplay, Start.AddTicks(-10), 100);
        await Grant("end", ProgressionXpSource.Gameplay, End, 100);
        await Grant("future", ProgressionXpSource.Gameplay, Start.AddDays(2), 100);
        Assert.AreEqual(3, await Repository.ReconcileAsync(await Season(), 100, Start));
        var season = await new MySqlSeasonProgressionRepository(_database).ReadAsync(Player, "s1", 1);
        Assert.AreEqual(70L, season.SeasonXp);
        var grant = await new MySqlSeasonProgressionRepository(_database).ReadGrantAsync(Player, "s1", "gameplay");
        Assert.IsNotNull(grant);
        Assert.AreEqual(20L, grant.AwardedXp);
        Assert.AreEqual("event", grant.BoostId);
        Assert.AreEqual(2m, grant.BoostMultiplier);
        Assert.AreEqual(470L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task RestartBoundedBatchAndLateGrants_AreDiscoverableForOfflinePlayers()
    {
        await Grant("first", ProgressionXpSource.Gameplay, Start, 10);
        await Grant("second", ProgressionXpSource.Gameplay, Start.AddDays(1), 20);
        Assert.AreEqual(1, await Repository.ReconcileAsync(await Season(), 1, End));
        Assert.AreEqual(1, await new MySqlSeasonRewardRepository(_database).ReconcileAsync(await Season(), 1, End));
        await Grant("late", ProgressionXpSource.ChallengeReward, Start, 30);
        Assert.AreEqual(1, await Repository.ReconcileAsync(await Season(), 1, End.AddDays(1)));
        Assert.AreEqual(0, await Repository.ReconcileAsync(await Season(), 1, End.AddDays(1)));
        Assert.AreEqual(60L, (await new MySqlSeasonProgressionRepository(_database).ReadAsync(Player, "s1", 1)).SeasonXp);
    }

    [TestMethod]
    public async Task ConcurrentReconciliation_NeverDoublePays()
    {
        for (var index = 0; index < 12; index++)
            await Grant($"event{index}", ProgressionXpSource.Gameplay, Start, 10);
        var season = await Season();
        var results = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => Repository.ReconcileAsync(season, 100, Start).AsTask()));
        Assert.AreEqual(12, results.Sum());
        var state = await new MySqlSeasonProgressionRepository(_database).ReadAsync(Player, "s1", 1);
        Assert.AreEqual(120L, state.SeasonXp);
        Assert.AreEqual(12L, state.Revision);
    }

    [TestMethod]
    public async Task ExplicitClosure_FreezesPendingAndHistoricalState()
    {
        await Grant("first", ProgressionXpSource.Gameplay, Start, 10);
        await Repository.ReconcileAsync(await Season(), 100, End);
        var closed = await new MySqlSeasonRepository(_database).CloseAsync("s1", 1, End);
        await Grant("late", ProgressionXpSource.Gameplay, Start, 20);
        Assert.AreEqual(0, await Repository.ReconcileAsync(closed, 100, End));
        Assert.AreEqual(10L, (await new MySqlSeasonProgressionRepository(_database).ReadAsync(Player, "s1", 1)).SeasonXp);
        Assert.HasCount(1, await Repository.ReadTopAsync("s1", 1, 0, 5));
    }

    [TestMethod]
    public async Task FailedGrant_RollsBackItselfAndRetryDrainsRemainingBatch()
    {
        await Grant("first", ProgressionXpSource.Gameplay, Start, 10);
        await Grant("second", ProgressionXpSource.Gameplay, Start.AddHours(1), 20);
        await Execute("CREATE TRIGGER fail_season_progression_grant BEFORE INSERT ON ano_progression_season_grants FOR EACH ROW BEGIN IF NEW.grant_id = 'second' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'test failure'; END IF; END");
        await Assert.ThrowsAsync<Exception>(async () => await Repository.ReconcileAsync(await Season(), 100, End));
        Assert.AreEqual(10L, (await new MySqlSeasonProgressionRepository(_database).ReadAsync(Player, "s1", 1)).SeasonXp);
        await Execute("DROP TRIGGER fail_season_progression_grant");
        Assert.AreEqual(1, await Repository.ReconcileAsync(await Season(), 100, End));
        Assert.AreEqual(30L, (await new MySqlSeasonProgressionRepository(_database).ReadAsync(Player, "s1", 1)).SeasonXp);
    }

    [TestMethod]
    public async Task Leaderboard_UsesIndependentXpAndDeterministicPlayerTieOrdering()
    {
        var other = new PlayerId(Player.SteamId64 + 1);
        await Grant("first", ProgressionXpSource.Gameplay, Start, 10, player: other);
        await Grant("first", ProgressionXpSource.Gameplay, Start, 10);
        await Grant("admin", ProgressionXpSource.Administration, Start, 1000);
        await Repository.ReconcileAsync(await Season(), 100, End);
        var top = await Repository.ReadTopAsync("s1", 1, 0, 1);
        Assert.HasCount(1, top);
        Assert.AreEqual(Player, top[0].PlayerId);
        Assert.AreEqual(1L, top[0].Placement);
        var second = await Repository.ReadTopAsync("s1", 1, 1, 1);
        Assert.AreEqual(other, second[0].PlayerId);
        Assert.AreEqual(2L, second[0].Placement);
        Assert.IsEmpty(await Repository.ReadTopAsync("s1", 1, 2, 5));
        Assert.IsEmpty(await Repository.ReadTopAsync("s1", 2, 0, 5));
    }

    [TestMethod]
    public async Task CancellationAndBounds_AreEnforced()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Repository.ReconcileAsync(await Season(), 100, Start, cancellation.Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await Repository.ReconcileAsync(await Season(), 101, Start));
        await Assert.ThrowsAsync<ArgumentException>(async () => await Repository.ReadTopAsync("invalid id", 1, 0, 5));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await Repository.ReadTopAsync("s1", 1, -1, 5));
    }

    private async Task Grant(string id, ProgressionXpSource source, DateTimeOffset at, long amount,
        decimal multiplier = 1, PlayerId? player = null)
        => _ = await new MySqlProgressionGrantRepository(_database).ApplyAsync(player ?? Player,
            new(id, source, amount, (long)(amount * multiplier), "test.reward", at,
                multiplier == 1 ? null : "event", multiplier));

    private async Task Execute(string sql)
        => _ = await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteNonQueryAsync(token);
        });

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var statement in new[]
            {
                "DROP TRIGGER IF EXISTS fail_season_progression_grant",
                "DROP TABLE IF EXISTS ano_progression_season_grants",
                "DROP TABLE IF EXISTS ano_progression_season_accounts",
                "DROP TABLE IF EXISTS ano_progression_season_runtime",
                "DROP TABLE IF EXISTS ano_progression_seasons",
                "DROP TABLE IF EXISTS ano_progression_challenges",
                "DROP TABLE IF EXISTS ano_progression_achievements",
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
