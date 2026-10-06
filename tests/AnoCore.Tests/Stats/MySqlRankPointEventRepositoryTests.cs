using AnoCore.Abstractions.Persistence;
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
    private static readonly DateTimeOffset Now =
        new(2026, 10, 6, 18, 0, 0, TimeSpan.Zero);
    private MySqlDatabase _database = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection))
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured for integration tests.");
        _database = new MySqlDatabase(connection!);
        await DropAsync();
        await new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004(),
            new PlaytimeSchemaMigration005(), new CombatSchemaMigration006(),
            new RankAdjustmentSchemaMigration007(), new PlaytimeStateSchemaMigration008(),
            new CombatDetailSchemaMigration009(), new GameplayStatSchemaMigration010(),
            new StatisticsResetSchemaMigration011(), new RankPointEventSchemaMigration018()
        ]).ApplyPendingAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    [TestMethod]
    public async Task RecordAsync_IsIdempotentAndRejectsConflictingReplay()
    {
        var player = new PlayerId(76561198000012501);
        var source = Guid.NewGuid();
        var repository = new MySqlRankPointEventRepository(_database);
        var award = new RankPointEvent(source, player, "kill", 8, Now, "de_dust2");

        Assert.IsTrue(await repository.RecordAsync(award));
        Assert.IsFalse(await repository.RecordAsync(
            new RankPointEvent(source, player, "kill", 8, Now, "de_dust2")));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await repository.RecordAsync(
                new RankPointEvent(source, player, "kill", 9, Now, "de_dust2")));
    }

    [TestMethod]
    public async Task LedgerScore_UsesEventsBaselineAdjustmentFloorAndStablePlacement()
    {
        var first = new PlayerId(76561198000012511);
        var second = new PlayerId(76561198000012512);
        var third = new PlayerId(76561198000012513);
        var profiles = new MySqlPlayerRepository(_database);
        foreach (var player in new[] { first, second, third })
            await profiles.UpsertAsync(
                new PlayerProfile(player, $"Player {player.SteamId64}", Now, Now));

        var ledger = new MySqlRankPointEventRepository(_database);
        var firstEvent = Guid.NewGuid();
        Assert.IsTrue(await ledger.RecordAsync(
            new RankPointEvent(firstEvent, first, "kill", 8, Now, "de_dust2")));
        Assert.IsTrue(await ledger.RecordAsync(
            new RankPointEvent(firstEvent, first, "headshot", 5, Now, "de_dust2")));
        Assert.IsTrue(await ledger.RecordAsync(
            new RankPointEvent(Guid.NewGuid(), second, "death", -5, Now, "de_dust2")));

        var adjustments = new MySqlRankAdjustmentRepository(_database);
        await adjustments.SetAsync(third, 3, null, Now);

        var weights = new RankScoreWeights(
            2, 1, 1, 10, scoringMode: RankScoringMode.EventLedger);
        var combat = new MySqlCombatRepository(_database);

        Assert.AreEqual(23L, await combat.ReadRawScoreAsync(first, weights));
        Assert.AreEqual(5L, await combat.ReadRawScoreAsync(second, weights));

        var top = await combat.GetTopScoresAsync(weights, 3, 0);
        CollectionAssert.AreEqual(new[] { first, third, second },
            top.Select(x => x.PlayerId).ToArray());
        CollectionAssert.AreEqual(new long[] { 23, 13, 5 },
            top.Select(x => x.Points).ToArray());

        await adjustments.SetAsync(second, -100, null, Now);
        var placement = await combat.GetScorePlacementAsync(second, weights);
        Assert.IsNotNull(placement);
        Assert.AreEqual(0L, placement.Points);
        Assert.AreEqual(3, placement.Position);
    }

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var view in new[] {
                "ano_effective_gameplay_stats", "ano_effective_combat_damage",
                "ano_effective_combat_weapon_fire", "ano_effective_combat_assists",
                "ano_effective_combat_deaths", "ano_effective_combat_kills"
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP VIEW IF EXISTS {view}";
                await command.ExecuteNonQueryAsync(token);
            }
            foreach (var table in new[] {
                "ano_rank_point_events", "ano_statistics_resets", "ano_gameplay_stats",
                "ano_combat_damage", "ano_combat_weapon_fire", "ano_rank_adjustments",
                "ano_playtime_segments", "ano_combat_deaths", "ano_playtime_sessions",
                "ano_admin_warnings", "ano_admin_action_audit", "ano_moderation_audit",
                "ano_moderation_sanctions", "ano_module_data", "ano_players",
                "ano_schema_migrations"
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE IF EXISTS {table}";
                await command.ExecuteNonQueryAsync(token);
            }
            return true;
        });
    }
}
