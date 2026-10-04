using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Auditing;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using AnoCore.Runtime.Stats;
using MySqlConnector;

namespace AnoCore.Tests.Stats;

[TestClass]
[DoNotParallelize]
public sealed class MySqlRankAdjustmentAdministrationServiceTests
{
    private static readonly PlayerId Target = new(76561198000013001);
    private static readonly PlayerId Actor = new(76561198000013002);
    private static readonly DateTimeOffset Now =
        new(2026, 9, 26, 18, 0, 0, TimeSpan.Zero);
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
            new RankAdjustmentSchemaMigration007()]).ApplyPendingAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    [TestMethod]
    public async Task GiveTakeSetReset_AreDurableAndAudited()
    {
        var service = new MySqlRankAdjustmentAdministrationService(_database);
        var repository = new MySqlRankAdjustmentRepository(_database);

        var given = await service.ApplyAsync(RankAdjustmentAdminOperation.Give,
            Target, 25, Actor, "manual reward", Now);
        Assert.AreEqual(0L, given.PreviousPoints);
        Assert.AreEqual(25L, given.CurrentPoints);

        var taken = await service.ApplyAsync(RankAdjustmentAdminOperation.Take,
            Target, 5, Actor, "correction", Now.AddMinutes(1));
        Assert.AreEqual(25L, taken.PreviousPoints);
        Assert.AreEqual(20L, taken.CurrentPoints);

        var set = await service.ApplyAsync(RankAdjustmentAdminOperation.Set,
            Target, -10, null, "console correction", Now.AddMinutes(2));
        Assert.AreEqual(-10L, set.CurrentPoints);
        Assert.AreEqual(-10L, (await repository.ReadAsync(Target))!.Points);

        var reset = await service.ApplyAsync(RankAdjustmentAdminOperation.Reset,
            Target, 0, Actor, "restore derived score", Now.AddMinutes(3));
        Assert.AreEqual(-10L, reset.PreviousPoints);
        Assert.AreEqual(0L, reset.CurrentPoints);
        Assert.IsNull(await repository.ReadAsync(Target));

        var audit = await new MySqlAdminAuditRepository(_database)
            .GetTargetHistoryAsync(Target, 10);
        CollectionAssert.AreEqual(
            new[] { "rank.adjustment.reset", "rank.adjustment.set",
                "rank.adjustment.take", "rank.adjustment.give" },
            audit.Select(x => x.Action.Value).ToArray());
        Assert.AreEqual("points=25; manual reward", audit[^1].Reason);
    }

    [TestMethod]
    public async Task DuplicateAuditId_RollsBackAdjustment()
    {
        var auditId = Guid.NewGuid();
        var service = new MySqlRankAdjustmentAdministrationService(_database, () => auditId);
        await service.ApplyAsync(RankAdjustmentAdminOperation.Set,
            Target, 10, Actor, "first", Now);

        await Assert.ThrowsExactlyAsync<MySqlException>(async () =>
            await service.ApplyAsync(RankAdjustmentAdminOperation.Give,
                Target, 5, Actor, "duplicate audit", Now.AddMinutes(1)));

        var stored = await new MySqlRankAdjustmentRepository(_database).ReadAsync(Target);
        Assert.IsNotNull(stored);
        Assert.AreEqual(10L, stored.Points);
    }

    [TestMethod]
    public async Task Apply_RejectsInvalidDeltasAndBounds()
    {
        var service = new MySqlRankAdjustmentAdministrationService(_database);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await service.ApplyAsync(RankAdjustmentAdminOperation.Give,
                Target, 0, Actor, "invalid", Now));
        await service.ApplyAsync(RankAdjustmentAdminOperation.Set, Target,
            MySqlRankAdjustmentRepository.MaximumAbsolutePoints, Actor, "maximum", Now);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await service.ApplyAsync(RankAdjustmentAdminOperation.Give,
                Target, 1, Actor, "overflow", Now));
    }

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var table in new[] {
                "ano_combat_damage", "ano_combat_weapon_fire", "ano_rank_adjustments", "ano_playtime_segments",
                "ano_combat_deaths", "ano_playtime_sessions", "ano_admin_warnings", "ano_admin_action_audit",
                "ano_moderation_audit", "ano_moderation_sanctions", "ano_module_data", "ano_players",
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
