using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using AnoCore.Runtime.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
[DoNotParallelize]
public sealed class MySqlPlaytimeRepositoryTests
{
    private static readonly PlayerId Player = new(76561198000011201);
    private static readonly PlayerId Other = new(76561198000011202);
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 23, 59, 30, TimeSpan.Zero);
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
    public async Task DuplicateAndOutOfOrderEvents_DoNotOvercountAfterRestart()
    {
        var runner = new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004(),
            new PlaytimeSchemaMigration005()]);
        Assert.AreEqual(5, await runner.ApplyPendingAsync());
        Assert.AreEqual(0, await runner.ApplyPendingAsync());

        var first = PlayerSessionId.New();
        var repository = new MySqlPlaytimeRepository(_database);
        await repository.OpenAsync(Player, first, Start);
        await repository.OpenAsync(Player, first, Start.AddHours(1));
        await repository.AdvanceAsync(Player, first, Start.AddMinutes(2));
        await repository.AdvanceAsync(Player, first, Start.AddMinutes(1));
        await repository.AdvanceAsync(Other, first, Start.AddHours(2));
        await repository.AdvanceAsync(Player, first, Start.AddMinutes(1), close: true);
        await repository.AdvanceAsync(Player, first, Start.AddHours(3));

        var restarted = new MySqlPlaytimeRepository(_database);
        var totals = await restarted.ReadAsync(Player, new DateOnly(2026, 9, 26));
        Assert.AreEqual(TimeSpan.FromMinutes(2), totals.Total);
        Assert.AreEqual(TimeSpan.FromSeconds(90), totals.Today);
        Assert.AreEqual(TimeSpan.Zero,
            (await restarted.ReadAsync(Player, new DateOnly(2026, 9, 27))).Today);

        var second = PlayerSessionId.New();
        await restarted.OpenAsync(Player, second, Start.AddMinutes(3));
        await restarted.AdvanceAsync(Player, second, Start.AddMinutes(4), close: true);
        var afterReconnect = await restarted.ReadAsync(Player, new DateOnly(2026, 9, 26));
        Assert.AreEqual(TimeSpan.FromMinutes(3), afterReconnect.Total);
        Assert.AreEqual(TimeSpan.FromSeconds(150), afterReconnect.Today);
    }

    [TestMethod]
    public async Task Toplist_OrdersTiesBySteamIdAndPaginatesAfterRestart()
    {
        await new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004(),
            new PlaytimeSchemaMigration005()]).ApplyPendingAsync();
        var repo = new MySqlPlaytimeRepository(_database);
        var low = new PlayerId(76561198000011210);
        var high = new PlayerId(76561198000011211);
        var leader = new PlayerId(76561198000011212);
        foreach (var (id, seconds) in new[] { (low, 60), (high, 60), (leader, 120) })
        {
            var session = PlayerSessionId.New();
            await repo.OpenAsync(id, session, Start);
            await repo.AdvanceAsync(id, session, Start.AddSeconds(seconds), close: true);
        }
        await repo.OpenAsync(Other, PlayerSessionId.New(), Start);

        var restarted = new MySqlPlaytimeRepository(_database);
        var firstPage = await restarted.GetTopAsync(2, 0);
        var secondPage = await restarted.GetTopAsync(2, 2);
        CollectionAssert.AreEqual(new[] { leader, low },
            firstPage.Select(entry => entry.PlayerId).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, firstPage.Select(entry => entry.Position).ToArray());
        Assert.AreEqual(TimeSpan.FromMinutes(2), firstPage[0].Total);
        Assert.AreEqual(high, secondPage.Single().PlayerId);
        Assert.AreEqual(3, secondPage.Single().Position);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await restarted.GetTopAsync(0, 0));
    }

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var table in new[] {
                "ano_playtime_sessions", "ano_admin_warnings", "ano_admin_action_audit",
                "ano_moderation_audit", "ano_moderation_sanctions", "ano_module_data",
                "ano_players", "ano_schema_migrations" })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE IF EXISTS {table}";
                await command.ExecuteNonQueryAsync(token);
            }
            return true;
        });
    }
}
