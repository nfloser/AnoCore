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
public sealed class MySqlChallengeRepositoryTests
{
    private static readonly PlayerId Player = new(76561198000258101);
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
    private MySqlDatabase _database = null!;
    private static ProgressionDefinitionSnapshot Xp => ProgressionDefinitionSnapshot.Create([new(1, 0)], []);
    private static ChallengeDefinition Daily(string id = "daily.headshots", long target = 10, long reward = 100,
        DateTimeOffset? start = null, IReadOnlyList<string>? prerequisites = null)
        => new(id, 1, "Headshots", ChallengeWindowKind.Daily, GameplayStatKind.HeadshotKill, target, reward,
            start ?? Start, (start ?? Start).AddDays(1), prerequisites ?? []);
    private MySqlChallengeRepository Repository => new(_database);

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured.");
        _database = new MySqlDatabase(connection!);
        await DropAsync();
        await new MigrationRunner(_database, [new GameplayStatSchemaMigration010()]).ApplyPendingAsync();
        await ProgressionPersistenceBootstrap.EnsureReadyAsync(_database);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    [TestMethod]
    public async Task WindowCounts_ExcludeOldFutureOtherPlayerAndOtherStatisticEvents()
    {
        var catalog = ChallengeCatalogSnapshot.Create([Daily(target: 2)]);
        await Record(Start.AddTicks(-10), 100);
        await Record(Start, 1);
        await Record(Start.AddHours(1), 1);
        await Record(Start.AddHours(2), 100);
        await Record(Start, 100, new PlayerId(Player.SteamId64 + 1));
        await Record(Start, 100, kind: GameplayStatKind.Mvp);
        var status = await Repository.ReadAsync(Player, catalog, "daily.headshots", Start.AddMinutes(30));
        Assert.AreEqual(1L, status.Progress);
        Assert.AreEqual(ChallengeEvaluationState.Active, status.State);
        var result = await Repository.CompleteAsync(Player, catalog, "daily.headshots", Start.AddHours(1), Xp);
        Assert.IsTrue(result.Applied);
        Assert.AreEqual(100L, result.Completion!.Grant.AwardedXp);
        Assert.AreEqual(ChallengeEvaluationState.Completed, result.Evaluation.State);
    }

    [TestMethod]
    public async Task Completion_IsRestartSafeAndRewardChangesCannotRepaySameOccurrence()
    {
        await Record(Start, 10);
        var catalog = ChallengeCatalogSnapshot.Create([Daily()]);
        var first = await Repository.CompleteAsync(Player, catalog, "daily.headshots", Start, Xp);
        var changed = ChallengeCatalogSnapshot.Create([Daily(reward: 999) with { Version = 2 }]);
        var retry = await Repository.CompleteAsync(Player, changed, "daily.headshots", Start.AddDays(2), Xp);
        Assert.IsTrue(first.Applied);
        Assert.IsFalse(retry.Applied);
        Assert.AreEqual(100L, retry.Completion!.Grant.AwardedXp);
        Assert.AreEqual(1, retry.Completion.DefinitionVersion);
        Assert.AreEqual(100L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task ConcurrentCompletions_PayExactlyOnce()
    {
        await Record(Start, 10);
        var catalog = ChallengeCatalogSnapshot.Create([Daily()]);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Repository.CompleteAsync(Player, catalog, "daily.headshots", Start, Xp).AsTask()));
        Assert.AreEqual(1, results.Count(result => result.Applied));
        Assert.AreEqual(100L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task Prerequisites_RequireCommittedCompletionOfConfiguredOccurrence()
    {
        await Record(Start, 10);
        var catalog = ChallengeCatalogSnapshot.Create([Daily("base"), Daily("dependent", prerequisites: ["base"])]);
        Assert.AreEqual(ChallengeEvaluationState.Locked,
            (await Repository.CompleteAsync(Player, catalog, "dependent", Start, Xp)).Evaluation.State);
        await Repository.CompleteAsync(Player, catalog, "base", Start, Xp);
        Assert.IsTrue((await Repository.CompleteAsync(Player, catalog, "dependent", Start, Xp)).Applied);
        var next = ChallengeCatalogSnapshot.Create([Daily("base", start: Start.AddDays(1)),
            Daily("dependent", start: Start.AddDays(1), prerequisites: ["base"])]);
        await Record(Start.AddDays(1), 10);
        Assert.AreEqual(ChallengeEvaluationState.Locked,
            (await Repository.CompleteAsync(Player, next, "dependent", Start.AddDays(1), Xp)).Evaluation.State);
        Assert.IsTrue((await Repository.CompleteAsync(Player, next, "base", Start.AddDays(1), Xp)).Applied);
    }

    [TestMethod]
    public async Task ExpiryAndFuture_DoNotPayEvenWhenTargetIsReached()
    {
        await Record(Start, 10);
        var catalog = ChallengeCatalogSnapshot.Create([Daily()]);
        Assert.AreEqual(ChallengeEvaluationState.Future,
            (await Repository.CompleteAsync(Player, catalog, "daily.headshots", Start.AddTicks(-10), Xp)).Evaluation.State);
        Assert.AreEqual(ChallengeEvaluationState.Expired,
            (await Repository.CompleteAsync(Player, catalog, "daily.headshots", Start.AddDays(1), Xp)).Evaluation.State);
        Assert.AreEqual(0L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task CompletionInsertFailure_RollsBackXpAndLedger()
    {
        await Record(Start, 10);
        await Sql("""
            CREATE TRIGGER fail_challenge_completion BEFORE INSERT ON ano_progression_challenges
            FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'test failure'
            """);
        var catalog = ChallengeCatalogSnapshot.Create([Daily()]);
        await Assert.ThrowsAsync<Exception>(async () =>
            await Repository.CompleteAsync(Player, catalog, "daily.headshots", Start, Xp));
        Assert.AreEqual(ChallengeEvaluationState.ReadyToComplete,
            (await Repository.ReadAsync(Player, catalog, "daily.headshots", Start)).State);
        var state = await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player);
        Assert.AreEqual(0L, state.LifetimeXp);
        Assert.AreEqual(0L, state.Revision);
        await Sql("DROP TRIGGER fail_challenge_completion");
        Assert.IsTrue((await Repository.CompleteAsync(Player, catalog, "daily.headshots", Start, Xp)).Applied);
    }

    [TestMethod]
    public async Task Overflow_RollsBackCompletionAndPreservesAccount()
    {
        await Record(Start, 10);
        var grants = new MySqlProgressionGrantRepository(_database);
        await grants.ApplyAsync(Player, new("seed", ProgressionXpSource.Administration, long.MaxValue, long.MaxValue,
            "test.seed", Start, null, 1));
        var catalog = ChallengeCatalogSnapshot.Create([Daily()]);
        await Assert.ThrowsExactlyAsync<OverflowException>(async () =>
            await Repository.CompleteAsync(Player, catalog, "daily.headshots", Start, Xp));
        Assert.AreEqual(ChallengeEvaluationState.ReadyToComplete,
            (await Repository.ReadAsync(Player, catalog, "daily.headshots", Start)).State);
        Assert.AreEqual(long.MaxValue, (await grants.ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task ChallengeBoost_RequiresExplicitOptInAndIsPreservedOnRetry()
    {
        await Record(Start, 10);
        var catalog = ChallengeCatalogSnapshot.Create([Daily()]);
        var boost = ProgressionDefinitionSnapshot.Create([new(1, 0)],
            [new("reward-event", Start, Start.AddDays(1), 2, ProgressionXpSourceMask.ChallengeReward)]);
        var result = await Repository.CompleteAsync(Player, catalog, "daily.headshots", Start, boost);
        Assert.AreEqual(200L, result.Completion!.Grant.AwardedXp);
        Assert.AreEqual("reward-event", result.Completion.Grant.BoostId);
        var retry = await Repository.CompleteAsync(Player, catalog, "daily.headshots", Start, Xp);
        Assert.AreEqual(200L, retry.Completion!.Grant.AwardedXp);
    }

    [TestMethod]
    public async Task StatisticReset_DoesNotEraseWindowProgress()
    {
        await Record(Start, 10);
        await Sql("""
            CREATE TABLE IF NOT EXISTS ano_statistics_resets
                (player_steam_id BIGINT UNSIGNED PRIMARY KEY, reset_at_utc DATETIME(6) NOT NULL,
                 updated_by_steam_id BIGINT UNSIGNED NULL);
            DELETE FROM ano_statistics_resets;
            INSERT INTO ano_statistics_resets (player_steam_id, reset_at_utc) VALUES (76561198000258101, '2026-10-09 01:00:00');
            CREATE OR REPLACE VIEW ano_effective_gameplay_stats AS
                SELECT g.* FROM ano_gameplay_stats g LEFT JOIN ano_statistics_resets r
                ON r.player_steam_id = g.player_steam_id
                WHERE r.reset_at_utc IS NULL OR g.occurred_at_utc > r.reset_at_utc
            """);
        Assert.IsEmpty(await new MySqlGameplayStatRepository(_database).ReadAsync(Player));
        var catalog = ChallengeCatalogSnapshot.Create([Daily()]);
        Assert.IsTrue((await Repository.CompleteAsync(Player, catalog, "daily.headshots", Start.AddHours(2), Xp)).Applied);
    }

    [TestMethod]
    public async Task PersistedOccurrence_RejectsChangedEndBoundaryAndSubMicrosecondWindows()
    {
        await Record(Start, 10);
        var catalog = ChallengeCatalogSnapshot.Create([Daily()]);
        await Repository.CompleteAsync(Player, catalog, "daily.headshots", Start, Xp);
        var changed = ChallengeCatalogSnapshot.Create([Daily() with
            { WindowKind = ChallengeWindowKind.Season, EndsAtUtc = Start.AddDays(2) }]);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await Repository.CompleteAsync(Player, changed, "daily.headshots", Start, Xp));
        var imprecise = ChallengeCatalogSnapshot.Create([Daily(start: Start.AddTicks(1))]);
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await Repository.CompleteAsync(Player, imprecise, "daily.headshots", Start, Xp));
    }

    [TestMethod]
    public async Task OrphanGrantCollision_RejectsCompletionWithoutChangingXp()
    {
        await Record(Start, 10);
        var grants = new MySqlProgressionGrantRepository(_database);
        var grantId = "challenge:daily.headshots:" + Start.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await grants.ApplyAsync(Player, new(grantId, ProgressionXpSource.ChallengeReward, 100, 100,
            "challenge.daily.headshots", Start, null, 1));
        var catalog = ChallengeCatalogSnapshot.Create([Daily()]);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await Repository.CompleteAsync(Player, catalog, "daily.headshots", Start, Xp));
        Assert.AreEqual(ChallengeEvaluationState.ReadyToComplete,
            (await Repository.ReadAsync(Player, catalog, "daily.headshots", Start)).State);
        Assert.AreEqual(100L, (await grants.ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task EndBoundary_ExcludesEventsAndDefaultGameplayBoostDoesNotBoostRewards()
    {
        var definition = Daily() with { WindowKind = ChallengeWindowKind.Weekly, EndsAtUtc = Start.AddDays(7) };
        var catalog = ChallengeCatalogSnapshot.Create([definition]);
        await Record(Start.AddDays(7), 10);
        Assert.AreEqual(0L, (await Repository.ReadAsync(Player, catalog, definition.Id, Start.AddDays(7))).Progress);
        await Record(Start.AddDays(6), 10);
        var gameplayBoost = ProgressionDefinitionSnapshot.Create([new(1, 0)],
            [new("weekend", Start, Start.AddDays(7), 2)]);
        var result = await Repository.CompleteAsync(Player, catalog, definition.Id, Start.AddDays(6), gameplayBoost);
        Assert.IsTrue(result.Applied);
        Assert.AreEqual(100L, result.Completion!.Grant.AwardedXp);
        Assert.IsNull(result.Completion.Grant.BoostId);
    }

    private async Task Record(DateTimeOffset at, int amount, PlayerId? player = null,
        GameplayStatKind kind = GameplayStatKind.HeadshotKill)
        => await new MySqlGameplayStatRepository(_database).RecordAsync(new(Guid.NewGuid(), player ?? Player,
            at, "de_dust2", kind, amount));

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
            "DROP TRIGGER IF EXISTS fail_challenge_completion",
            "DROP TABLE IF EXISTS ano_progression_challenges",
            "DROP TABLE IF EXISTS ano_progression_achievements",
            "DROP TABLE IF EXISTS ano_progression_season_grants",
            "DROP TABLE IF EXISTS ano_progression_season_accounts",
            "DROP TABLE IF EXISTS ano_progression_season_runtime",
            "DROP TABLE IF EXISTS ano_progression_seasons",
            "DROP TABLE IF EXISTS ano_progression_grants",
            "DROP TABLE IF EXISTS ano_progression_accounts",
            "DROP TABLE IF EXISTS ano_gameplay_stats",
            "DROP TABLE IF EXISTS ano_schema_migrations",
        }) await Sql(sql);
    }
}
