using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using AnoCore.Runtime.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
[DoNotParallelize]
public sealed class MySqlCombatRepositoryTests
{
    private static readonly PlayerId Victim = new(76561198000012201);
    private static readonly PlayerId Attacker = new(76561198000012202);
    private static readonly PlayerId Assister = new(76561198000012203);
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
    private MySqlDatabase _database = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection))
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured for integration tests.");
        _database = new MySqlDatabase(connection!);
        await DropAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    [TestMethod]
    public async Task ReplayedEventsAndRestart_DoNotDuplicateCounters()
    {
        var migrations = new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004(),
            new PlaytimeSchemaMigration005(), new CombatSchemaMigration006()]);
        Assert.AreEqual(6, await migrations.ApplyPendingAsync());
        Assert.AreEqual(0, await migrations.ApplyPendingAsync());

        var repo = new MySqlCombatRepository(_database);
        var death = new CombatDeath(Guid.NewGuid(), Victim, Attacker, Assister, Now);
        await repo.RecordAsync(death);
        await repo.RecordAsync(new CombatDeath(death.EventId, Victim, Attacker, Assister,
            Now.AddMilliseconds(20)));
        var restarted = new MySqlCombatRepository(_database);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await restarted.RecordAsync(new CombatDeath(death.EventId, Victim, Attacker, null, Now)));
        Assert.AreEqual(new CombatTotals(1, 0, 0), await restarted.ReadAsync(Attacker));
        Assert.AreEqual(new CombatTotals(0, 1, 0), await restarted.ReadAsync(Victim));
        Assert.AreEqual(new CombatTotals(0, 0, 1), await restarted.ReadAsync(Assister));
    }

    [TestMethod]
    public async Task SuicideWorldDeathAndTeamKill_CountDeathsOnly()
    {
        await new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004(),
            new PlaytimeSchemaMigration005(), new CombatSchemaMigration006()]).ApplyPendingAsync();
        var repo = new MySqlCombatRepository(_database);
        await repo.RecordAsync(new CombatDeath(Guid.NewGuid(), Victim, Victim, null, Now));
        await repo.RecordAsync(new CombatDeath(Guid.NewGuid(), Victim, null, null, Now.AddSeconds(1)));
        await repo.RecordAsync(new CombatDeath(Guid.NewGuid(), Victim, Attacker, Assister,
            Now.AddSeconds(2), isTeamKill: true));
        Assert.AreEqual(new CombatTotals(0, 3, 0), await repo.ReadAsync(Victim));
        Assert.AreEqual(new CombatTotals(0, 0, 0), await repo.ReadAsync(Attacker));
        Assert.AreEqual(new CombatTotals(0, 0, 0), await repo.ReadAsync(Assister));
    }

    [TestMethod]
    public async Task KillLeaderboard_SortsTiesAndPagesAcrossRepositoryRestart()
    {
        await new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004(),
            new PlaytimeSchemaMigration005(), new CombatSchemaMigration006()]).ApplyPendingAsync();
        var repo = new MySqlCombatRepository(_database);
        var lower = new PlayerId(76561198000012001);
        var higher = new PlayerId(76561198000012002);
        await repo.RecordAsync(new CombatDeath(Guid.NewGuid(), Victim, higher, null, Now));
        await repo.RecordAsync(new CombatDeath(Guid.NewGuid(), Victim, lower, null, Now.AddSeconds(1)));
        await repo.RecordAsync(new CombatDeath(Guid.NewGuid(), Victim, Attacker, null, Now.AddSeconds(2)));
        await repo.RecordAsync(new CombatDeath(Guid.NewGuid(), Victim, Attacker, null, Now.AddSeconds(3)));
        await repo.RecordAsync(new CombatDeath(Guid.NewGuid(), Victim, Attacker, null,
            Now.AddSeconds(4), isTeamKill: true));

        var restarted = new MySqlCombatRepository(_database);
        var first = await restarted.GetTopKillsAsync(2, 0);
        CollectionAssert.AreEqual(new[] { Attacker, lower }, first.Select(x => x.PlayerId).ToArray());
        CollectionAssert.AreEqual(new long[] { 2, 1 }, first.Select(x => x.Kills).ToArray());
        var second = await restarted.GetTopKillsAsync(2, 2);
        Assert.AreEqual(higher, second.Single().PlayerId);
        Assert.AreEqual(3, second.Single().Position);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await restarted.GetTopKillsAsync(0, 0));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await restarted.GetTopKillsAsync(5, 10001));
    }

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var table in new[] {
                "ano_combat_deaths", "ano_playtime_sessions", "ano_admin_warnings",
                "ano_admin_action_audit", "ano_moderation_audit", "ano_moderation_sanctions",
                "ano_module_data", "ano_players", "ano_schema_migrations" })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE IF EXISTS {table}";
                await command.ExecuteNonQueryAsync(token);
            }
            return true;
        });
    }
}
