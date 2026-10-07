using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using AnoCore.Runtime.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
[DoNotParallelize]
public sealed class MySqlRankPointEventRepositoryTests
{
    private static readonly PlayerId Player = new(76561198000278101);
    private static readonly PlayerId Other = new(Player.SteamId64 + 1);
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private MySqlDatabase _database = null!;
    private static RankScoreWeights Ledger(long baseline = 0) => new(2, 1, 1, baseline, null, RankScoreSource.EventLedger);
    private static RankPointEventBatch Batch(Guid? id = null, long first = 10, long second = -5)
        => RankPointEventBatch.Create(id ?? Guid.NewGuid(), "combat.death", Now, [new(Player, first), new(Other, second)]);

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured.");
        _database = new MySqlDatabase(connection!);
        await DropAsync();
        await new MigrationRunner(_database,
        [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(), new AdminAuditSchemaMigration003(),
            new WarningSchemaMigration004(), new PlaytimeSchemaMigration005(), new CombatSchemaMigration006(),
            new RankAdjustmentSchemaMigration007(), new PlaytimeStateSchemaMigration008(), new CombatDetailSchemaMigration009(),
            new GameplayStatSchemaMigration010(), new StatisticsResetSchemaMigration011(), new RankPointEventSchemaMigration018(),
        ]).ApplyPendingAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    [TestMethod]
    public async Task ConcurrentReplays_CommitOneCompleteBatchAndSurviveRestart()
    {
        var batch = Batch();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            new MySqlRankPointEventRepository(_database).ApplyAsync(batch).AsTask()));
        Assert.AreEqual(1, results.Count(result => result.Applied));
        var stored = await new MySqlRankPointEventRepository(_database).ReadAsync(batch.EventId);
        Assert.IsNotNull(stored);
        CollectionAssert.AreEqual(batch.Awards.ToArray(), stored.Awards.ToArray());
        Assert.AreEqual(batch.Source, stored.Source);
        Assert.AreEqual(Now, stored.OccurredAtUtc);
        var scores = new MySqlCombatRepository(_database);
        Assert.AreEqual(10L, await scores.ReadRawScoreAsync(Player, Ledger()));
        Assert.AreEqual(-5L, await scores.ReadRawScoreAsync(Other, Ledger()));
        Assert.IsFalse((await new MySqlRankPointEventRepository(_database).ApplyAsync(batch)).Applied);
    }

    [TestMethod]
    public async Task ConflictingReplay_RejectsDifferentPointsSourceTimestampOrParticipantSet()
    {
        var repository = new MySqlRankPointEventRepository(_database);
        var original = Batch();
        await repository.ApplyAsync(original);
        foreach (var conflict in new[]
        {
            Batch(original.EventId, 11),
            RankPointEventBatch.Create(original.EventId, "objective", Now, original.Awards),
            RankPointEventBatch.Create(original.EventId, original.Source, Now.AddSeconds(1), original.Awards),
            RankPointEventBatch.Create(original.EventId, original.Source, Now, [new(Player, 10)]),
        })
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await repository.ApplyAsync(conflict));
        Assert.AreEqual(10L, await new MySqlCombatRepository(_database).ReadRawScoreAsync(Player, Ledger()));
    }

    [TestMethod]
    public async Task LaterAwardFailure_RollsBackHeaderAndAllParticipantsAndAllowsRetry()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE TRIGGER fail_rank_event BEFORE INSERT ON ano_rank_point_events
                FOR EACH ROW BEGIN
                    IF NEW.player_steam_id = {Other.SteamId64} THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'test failure'; END IF;
                END
                """;
            await command.ExecuteNonQueryAsync(token);
            return true;
        });
        var repository = new MySqlRankPointEventRepository(_database);
        var batch = Batch();
        await Assert.ThrowsAsync<Exception>(async () => await repository.ApplyAsync(batch));
        Assert.IsNull(await repository.ReadAsync(batch.EventId));
        Assert.AreEqual(0L, await new MySqlCombatRepository(_database).ReadRawScoreAsync(Player, Ledger()));
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TRIGGER fail_rank_event";
            await command.ExecuteNonQueryAsync(token);
            return true;
        });
        Assert.IsTrue((await repository.ApplyAsync(batch)).Applied);
    }

    [TestMethod]
    public async Task Queries_CombineOnlySelectedSourceBaselineAndAdjustmentBeforeFlooring()
    {
        var events = new MySqlRankPointEventRepository(_database);
        await events.ApplyAsync(Batch(first: 20, second: -30));
        var scores = new MySqlCombatRepository(_database);
        await scores.RecordAsync(new CombatDeath(Guid.NewGuid(), Other, Player, null, Now));
        var adjustments = new MySqlRankAdjustmentRepository(_database);
        await adjustments.SetAsync(Player, -5, null, Now);
        await adjustments.SetAsync(Other, 40, null, Now);
        Assert.AreEqual(30L, await scores.ReadRawScoreAsync(Player, Ledger(10)));
        Assert.AreEqual(25L, (await scores.GetScorePlacementAsync(Player, Ledger(10)))!.Points);
        Assert.AreEqual(20L, (await scores.GetScorePlacementAsync(Other, Ledger(10)))!.Points);
        Assert.AreEqual(2L, await scores.ReadRawScoreAsync(Player, new RankScoreWeights(2, 1, 1)));
        await new MySqlRankAdjustmentAdministrationService(_database).ApplyAsync(
            RankAdjustmentAdminOperation.Reset, Other, 0, null, "reset adjustment", Now);
        Assert.AreEqual(0L, (await scores.GetScorePlacementAsync(Other, Ledger(10)))!.Points);
        Assert.AreEqual(-20L, await scores.ReadRawScoreAsync(Other, Ledger(10)));
    }

    [TestMethod]
    public async Task Placements_AreDeterministicPaginatedAndStatisticsResetLeavesLedgerIntact()
    {
        await new MySqlRankPointEventRepository(_database).ApplyAsync(Batch(first: 10, second: 10));
        var scores = new MySqlCombatRepository(_database);
        var top = await scores.GetTopScoresAsync(Ledger(), 1, 1);
        Assert.AreEqual(Other, top.Single().PlayerId);
        Assert.AreEqual(2, top.Single().Position);
        Assert.AreEqual(1, (await scores.GetScorePlacementAsync(Player, Ledger()))!.Position);
        await new MySqlStatisticsResetAdministrationService(_database).ResetAsync(Player, null, "test", Now.AddSeconds(1));
        Assert.AreEqual(10L, await scores.ReadRawScoreAsync(Player, Ledger()));
        Assert.AreEqual(10L, (await scores.GetScorePlacementAsync(Player, Ledger()))!.Points);
    }

    [TestMethod]
    public async Task DistinctConcurrentBatches_AccumulateWithoutLostAwards()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            new MySqlRankPointEventRepository(_database).ApplyAsync(Batch()).AsTask()));
        Assert.IsTrue(results.All(result => result.Applied));
        Assert.AreEqual(80L, await new MySqlCombatRepository(_database).ReadRawScoreAsync(Player, Ledger()));
        Assert.AreEqual(-40L, await new MySqlCombatRepository(_database).ReadRawScoreAsync(Other, Ledger()));
    }

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var sql in new[]
            {
                "DROP TRIGGER IF EXISTS fail_rank_event",
                "DROP TABLE IF EXISTS ano_rank_point_events", "DROP TABLE IF EXISTS ano_rank_point_batches",
                "DROP TABLE IF EXISTS ano_statistics_resets", "DROP TABLE IF EXISTS ano_gameplay_stats",
                "DROP TABLE IF EXISTS ano_combat_damage", "DROP TABLE IF EXISTS ano_combat_weapon_fire",
                "DROP TABLE IF EXISTS ano_rank_adjustments", "DROP TABLE IF EXISTS ano_combat_deaths",
                "DROP TABLE IF EXISTS ano_playtime_segments", "DROP TABLE IF EXISTS ano_playtime_sessions",
                "DROP TABLE IF EXISTS ano_admin_warnings", "DROP TABLE IF EXISTS ano_admin_action_audit",
                "DROP TABLE IF EXISTS ano_moderation_audit", "DROP TABLE IF EXISTS ano_moderation_sanctions",
                "DROP TABLE IF EXISTS ano_module_data", "DROP TABLE IF EXISTS ano_players",
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
