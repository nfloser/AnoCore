using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Progression;
using AnoCore.Modules.Progression.Persistence;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using AnoCore.Runtime.Stats;

namespace AnoCore.Tests.Progression;

[TestClass]
[DoNotParallelize]
public sealed class MySqlGameplayXpRepositoryTests
{
    private static readonly PlayerId Player = new(76561198000262101);
    private static readonly PlayerId Other = new(Player.SteamId64 + 1);
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
    private MySqlDatabase _database = null!;
    private static ProgressionDefinitionSnapshot Xp => ProgressionDefinitionSnapshot.Create([new(1, 0)], []);
    private static GameplayXpPolicy Policy(int batch = 100)
    {
        var configuration = new GameplayXpConfiguration
        {
            EarnFromUtc = Start,
            BatchSize = batch,
        };
        return configuration.Snapshot();
    }
    private MySqlGameplayXpRepository Repository => new(_database);

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured.");
        _database = new MySqlDatabase(connection!);
        await DropAsync();
        await new MigrationRunner(_database, [new CombatSchemaMigration006(), new GameplayStatSchemaMigration010()]).ApplyPendingAsync();
        await ProgressionPersistenceBootstrap.EnsureReadyAsync(_database);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    [TestMethod]
    public async Task Eligibility_UsesPlayerTypeTimeAndAcceptedCombatRoles()
    {
        await Combat(Other, Player, null, Start);
        await Combat(Other, new(Player.SteamId64 + 2), Player, Start);
        await Combat(Other, Player, null, Start, team: true);
        await Combat(Player, Player, null, Start);
        await Stat(Start, GameplayStatKind.HeadshotKill, 2);
        await Stat(Start.AddTicks(-10), GameplayStatKind.BombPlanted);
        await Stat(Start.AddHours(1), GameplayStatKind.BombPlanted);
        await Stat(Start, GameplayStatKind.HostageKilled);
        await Stat(Start, GameplayStatKind.BombPlanted, player: Other);
        var results = await Repository.ReconcileAsync(Player, Policy(), Xp, Start);
        Assert.HasCount(3, results);
        Assert.AreEqual(25L, results.Sum(grant => grant.AwardedXp));
        Assert.IsTrue(results.All(grant => grant.Source == ProgressionXpSource.Gameplay));
        Assert.AreEqual(25L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task Batches_AreBoundedAndLateEventsAreNotSkipped()
    {
        await Stat(Start, GameplayStatKind.Mvp);
        await Stat(Start.AddMinutes(1), GameplayStatKind.Mvp);
        Assert.HasCount(1, await Repository.ReconcileAsync(Player, Policy(1), Xp, Start.AddHours(1)));
        Assert.HasCount(1, await Repository.ReconcileAsync(Player, Policy(1), Xp, Start.AddHours(1)));
        Assert.IsEmpty(await Repository.ReconcileAsync(Player, Policy(1), Xp, Start.AddHours(1)));
        await Stat(Start, GameplayStatKind.BombPlanted);
        Assert.HasCount(1, await Repository.ReconcileAsync(Player, Policy(1), Xp, Start.AddHours(1)));
        Assert.AreEqual(40L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task RetryAndConfigurationChanges_DoNotRescoreCommittedEvents()
    {
        var statistic = new GameplayStatEvent(Guid.NewGuid(), Player, Start, "de_dust2", GameplayStatKind.BombPlanted);
        await new MySqlGameplayStatRepository(_database).RecordAsync(statistic);
        Assert.HasCount(1, await Repository.ReconcileAsync(Player, Policy(), Xp, Start));
        await new MySqlGameplayStatRepository(_database).RecordAsync(statistic);
        var changed = new GameplayXpConfiguration
        {
            EarnFromUtc = Start,
            GameplayXp = new() { [GameplayStatKind.BombPlanted] = 999 },
        }.Snapshot();
        Assert.IsEmpty(await new MySqlGameplayXpRepository(_database).ReconcileAsync(Player, changed, Xp, Start));
        Assert.AreEqual(20L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task ConcurrentBatches_AwardEventsExactlyOnce()
    {
        for (var index = 0; index < 10; index++) await Stat(Start, GameplayStatKind.Mvp);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Repository.ReconcileAsync(Player, Policy(), Xp, Start).AsTask()));
        Assert.AreEqual(10, results.Sum(grants => grants.Count));
        Assert.AreEqual(100L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task Boost_UsesEventTimeInsteadOfDelayedCheckpointTime()
    {
        await Stat(Start, GameplayStatKind.Mvp);
        await Stat(Start.AddDays(2), GameplayStatKind.Mvp);
        var definitions = ProgressionDefinitionSnapshot.Create([new(1, 0)],
            [new("weekend", Start, Start.AddDays(2), 2)]);
        var result = await Repository.ReconcileAsync(Player, Policy(), definitions, Start.AddDays(3));
        Assert.AreEqual(20L, result[0].AwardedXp);
        Assert.AreEqual("weekend", result[0].BoostId);
        Assert.AreEqual(Start, result[0].OccurredAtUtc);
        Assert.AreEqual(10L, result[1].AwardedXp);
        Assert.IsNull(result[1].BoostId);
    }

    [TestMethod]
    public async Task LaterGrantFailure_RollsBackEntireBatchAndAllowsRetry()
    {
        await Stat(Start, GameplayStatKind.Mvp);
        await Stat(Start.AddMinutes(1), GameplayStatKind.Mvp);
        await Sql("""
            CREATE TRIGGER fail_gameplay_xp BEFORE INSERT ON ano_progression_grants
            FOR EACH ROW BEGIN
                IF NEW.account_revision_after = 2 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'test failure'; END IF;
            END
            """);
        await Assert.ThrowsAsync<Exception>(async () => await Repository.ReconcileAsync(Player, Policy(), Xp, Start.AddHours(1)));
        var state = await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player);
        Assert.AreEqual(0L, state.LifetimeXp);
        Assert.AreEqual(0L, state.Revision);
        await Sql("DROP TRIGGER fail_gameplay_xp");
        Assert.HasCount(2, await Repository.ReconcileAsync(Player, Policy(), Xp, Start.AddHours(1)));
    }

    [TestMethod]
    public async Task Overflow_RollsBackGameplayGrant()
    {
        await Stat(Start, GameplayStatKind.Mvp);
        var grants = new MySqlProgressionGrantRepository(_database);
        await grants.ApplyAsync(Player, new("seed", ProgressionXpSource.Administration, long.MaxValue,
            long.MaxValue, "test.seed", Start, null, 1));
        await Assert.ThrowsExactlyAsync<OverflowException>(async () => await Repository.ReconcileAsync(Player, Policy(), Xp, Start));
        Assert.AreEqual(long.MaxValue, (await grants.ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task CancellationAndZeroWeights_DoNotAwardXp()
    {
        await Stat(Start, GameplayStatKind.Mvp);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Repository.ReconcileAsync(Player, Policy(), Xp, Start, cancelled.Token));
        var disabled = new GameplayXpConfiguration
        {
            EarnFromUtc = Start,
            KillXp = 0,
            AssistXp = 0,
            GameplayXp = [],
        }.Snapshot();
        Assert.IsEmpty(await Repository.ReconcileAsync(Player, disabled, Xp, Start));
        Assert.AreEqual(0L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task RecurringWeekend_AwardsDelayedEventsAtOriginalTimeAndNeverRepays()
    {
        var saturday = Start.AddDays(1);
        await Stat(saturday, GameplayStatKind.Mvp);
        await Stat(saturday.AddDays(2), GameplayStatKind.Mvp);
        var policy = new GameplayXpConfiguration { EarnFromUtc = Start, WeekendMultiplier = 2 }.Snapshot();
        var result = await Repository.ReconcileAsync(Player, policy, Xp, saturday.AddDays(3));
        Assert.AreEqual(20L, result[0].AwardedXp);
        Assert.AreEqual("gameplay.weekend.20261010", result[0].BoostId);
        Assert.AreEqual(saturday, result[0].OccurredAtUtc);
        Assert.AreEqual(10L, result[1].AwardedXp);
        var changed = new GameplayXpConfiguration { EarnFromUtc = Start, WeekendMultiplier = 3 }.Snapshot();
        Assert.IsEmpty(await Repository.ReconcileAsync(Player, changed, Xp, saturday.AddDays(10)));
        Assert.AreEqual(30L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
    }

    private async Task Stat(DateTimeOffset at, GameplayStatKind kind, int amount = 1, PlayerId? player = null)
        => await new MySqlGameplayStatRepository(_database).RecordAsync(new(Guid.NewGuid(), player ?? Player, at, "de_dust2", kind, amount));
    private async Task Combat(PlayerId victim, PlayerId? attacker, PlayerId? assister, DateTimeOffset at, bool team = false)
        => await new MySqlCombatRepository(_database).RecordAsync(new(Guid.NewGuid(), victim, attacker, assister, at, team));
    private async Task Sql(string sql)
        => _ = await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteNonQueryAsync(token);
        });
    private async Task DropAsync()
    {
        foreach (var sql in new[]
        {
            "DROP TRIGGER IF EXISTS fail_gameplay_xp",
            "DROP TABLE IF EXISTS ano_progression_challenges",
            "DROP TABLE IF EXISTS ano_progression_achievements",
            "DROP TABLE IF EXISTS ano_progression_season_grants",
            "DROP TABLE IF EXISTS ano_progression_season_accounts",
            "DROP TABLE IF EXISTS ano_progression_season_runtime",
            "DROP TABLE IF EXISTS ano_progression_seasons",
            "DROP TABLE IF EXISTS ano_progression_grants",
            "DROP TABLE IF EXISTS ano_progression_accounts",
            "DROP TABLE IF EXISTS ano_gameplay_stats",
            "DROP TABLE IF EXISTS ano_combat_deaths",
            "DROP TABLE IF EXISTS ano_schema_migrations",
        }) await Sql(sql);
    }
}
