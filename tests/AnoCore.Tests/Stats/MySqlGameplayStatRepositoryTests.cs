using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using AnoCore.Runtime.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
[DoNotParallelize]
public sealed class MySqlGameplayStatRepositoryTests
{
    private static readonly PlayerId Player = new(76561198000184011);
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 16, 30, 0, TimeSpan.Zero);
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
            new CombatDetailSchemaMigration009(), new GameplayStatSchemaMigration010()])
            .ApplyPendingAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    [TestMethod]
    public async Task Replay_IsIdempotentAndConflictingPayloadFailsAfterRestart()
    {
        var repository = new MySqlGameplayStatRepository(_database);
        var stat = new GameplayStatEvent(Guid.NewGuid(), Player, Now,
            "de_dust2", GameplayStatKind.BombPlanted);

        await repository.RecordAsync(stat);
        await repository.RecordAsync(new GameplayStatEvent(
            stat.EventId, Player, Now.AddMilliseconds(20),
            "de_dust2", GameplayStatKind.BombPlanted));

        var restarted = new MySqlGameplayStatRepository(_database);
        var totals = await restarted.ReadAsync(Player);
        Assert.AreEqual(1L, totals.Single(x =>
            x.Kind == GameplayStatKind.BombPlanted).Count);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await restarted.RecordAsync(new GameplayStatEvent(
                stat.EventId, Player, Now, "de_dust2", GameplayStatKind.BombDefused)));
    }

    [TestMethod]
    public async Task Read_GroupsKindsAndFiltersByMap()
    {
        var repository = new MySqlGameplayStatRepository(_database);
        await repository.RecordAsync(new GameplayStatEvent(
            Guid.NewGuid(), Player, Now, "de_dust2", GameplayStatKind.GrenadeThrown));
        await repository.RecordAsync(new GameplayStatEvent(
            Guid.NewGuid(), Player, Now.AddSeconds(1), "de_dust2",
            GameplayStatKind.GrenadeThrown, 2));
        await repository.RecordAsync(new GameplayStatEvent(
            Guid.NewGuid(), Player, Now.AddSeconds(2), "de_nuke", GameplayStatKind.Mvp));

        var all = await repository.ReadAsync(Player);
        Assert.AreEqual(3L, all.Single(x =>
            x.Kind == GameplayStatKind.GrenadeThrown).Count);
        Assert.AreEqual(1L, all.Single(x => x.Kind == GameplayStatKind.Mvp).Count);

        var dust = await repository.ReadAsync(
            Player, new GameplayStatFilter(" de_dust2 "));
        Assert.AreEqual(1, dust.Count);
        Assert.AreEqual(GameplayStatKind.GrenadeThrown, dust.Single().Kind);
        Assert.AreEqual(3L, dust.Single().Count);
    }

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var table in new[] {
                "ano_gameplay_stats", "ano_combat_damage", "ano_combat_weapon_fire",
                "ano_rank_adjustments", "ano_playtime_segments", "ano_combat_deaths",
                "ano_playtime_sessions", "ano_admin_warnings", "ano_admin_action_audit",
                "ano_moderation_audit", "ano_moderation_sanctions", "ano_module_data",
                "ano_players", "ano_schema_migrations"
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
