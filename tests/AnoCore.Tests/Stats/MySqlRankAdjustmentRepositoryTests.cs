using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using AnoCore.Runtime.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
[DoNotParallelize]
public sealed class MySqlRankAdjustmentRepositoryTests
{
    private static readonly PlayerId Player = new(76561198000012901);
    private static readonly PlayerId Actor = new(76561198000012902);
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
    public async Task SetOverwriteRestartAndReset_AreDurableForOfflinePlayer()
    {
        var first = new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero);
        var repository = new MySqlRankAdjustmentRepository(_database);
        Assert.IsNull(await repository.ReadAsync(Player));

        await repository.SetAsync(Player, 25, Actor, first);
        var restarted = new MySqlRankAdjustmentRepository(_database);
        var stored = await restarted.ReadAsync(Player);
        Assert.IsNotNull(stored);
        Assert.AreEqual(25L, stored.Points);
        Assert.AreEqual(Actor, stored.UpdatedBy);
        Assert.AreEqual(first, stored.UpdatedAtUtc);

        await restarted.SetAsync(Player, -10, null, first.AddMinutes(1));
        stored = await repository.ReadAsync(Player);
        Assert.IsNotNull(stored);
        Assert.AreEqual(-10L, stored.Points);
        Assert.IsNull(stored.UpdatedBy);

        await repository.ResetAsync(Player);
        Assert.IsNull(await restarted.ReadAsync(Player));
        await repository.ResetAsync(Player);
    }

    [TestMethod]
    public async Task Set_RejectsOutOfRangePoints()
    {
        var repository = new MySqlRankAdjustmentRepository(_database);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await repository.SetAsync(Player,
                MySqlRankAdjustmentRepository.MaximumAbsolutePoints + 1, Actor,
                DateTimeOffset.UtcNow));
    }

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var table in new[] {
                "ano_gameplay_stats", "ano_combat_damage", "ano_combat_weapon_fire", "ano_rank_adjustments", "ano_playtime_segments",
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
