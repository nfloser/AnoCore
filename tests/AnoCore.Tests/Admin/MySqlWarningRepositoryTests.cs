using System.Data.Common;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Warnings;
using AnoCore.Runtime.Composition;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using AnoCore.Runtime.Players;
using AnoCore.Runtime.Warnings;

namespace AnoCore.Tests.Admin;

[TestClass]
[DoNotParallelize]
public sealed class MySqlWarningRepositoryTests
{
    private static readonly PlayerId Target = new(76561198000009101);
    private static readonly PlayerId Actor = new(76561198000009102);
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private MySqlDatabase _database = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection))
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured for integration tests.");
        _database = new MySqlDatabase(connection!);
        await DropTablesAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropTablesAsync();
    }

    [TestMethod]
    public async Task MigrationRestartExpiryAndAtomicClear_PreserveWarningHistory()
    {
        var runner = new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004()]);
        Assert.AreEqual(4, await runner.ApplyPendingAsync());
        Assert.AreEqual(0, await runner.ApplyPendingAsync());

        var service = new WarningService(new MySqlWarningRepository(_database));
        var first = await service.WarnAsync(Target, null, "quote ' OR 1=1 --", Now, Now.AddMinutes(10));
        var second = await service.WarnAsync(Target, Actor, "second", Now.AddMinutes(1));
        var restarted = new WarningService(new MySqlWarningRepository(_database));

        Assert.AreEqual(2, (await restarted.GetActiveAsync(Target, Now.AddMinutes(2))).Count);
        Assert.AreEqual(1, (await restarted.GetActiveAsync(Target, Now.AddMinutes(10))).Count);
        var cleared = await restarted.ClearAsync(Target, null, "resolved", Now.AddMinutes(3));
        Assert.AreEqual(2, cleared.Count);
        Assert.AreEqual(0, (await restarted.ClearAsync(Target, Actor, "again", Now.AddMinutes(4))).Count);

        var history = await restarted.GetHistoryAsync(Target);
        Assert.AreEqual(2, history.Count);
        Assert.AreEqual(first.Id, history[0].Id);
        Assert.AreEqual(second.Id, history[1].Id);
        Assert.AreEqual("quote ' OR 1=1 --", history[0].Reason);
        Assert.AreEqual("resolved", history[0].ClearReason);
        Assert.IsNull(history[0].ClearedById);
        Assert.AreEqual(0, (await restarted.GetActiveAsync(Target, Now.AddMinutes(4))).Count);
    }

    [TestMethod]
    public async Task RuntimeServices_ProvidesSharedWarningService()
    {
        var events = new AnoEventBus();
        var path = Path.Combine(Path.GetTempPath(), "ano-warnings-" + Guid.NewGuid().ToString("N"));
        using var runtime = await RuntimeServices.CreateAsync(
            _database, new JsonConfigStore(path), events, new PlayerRegistry(events));
        Assert.AreSame(runtime.Warnings, runtime.GetService(typeof(IWarningService)));
        Assert.AreSame(runtime.WarningRepository, runtime.GetService(typeof(IWarningRepository)));
        await runtime.Warnings.WarnAsync(Target, Actor, "persistent", Now);
        Assert.AreEqual(1, (await runtime.Warnings.GetHistoryAsync(Target)).Count);
    }

    [TestMethod]
    public async Task DuplicateId_FailsWithoutOverwritingWarning()
    {
        await new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004()])
            .ApplyPendingAsync();
        var repo = new MySqlWarningRepository(_database);
        var id = Guid.NewGuid();
        await repo.InsertAsync(new WarningRecord(id, Target, Actor, "original", Now));
        await Assert.ThrowsAsync<DbException>(async () =>
            await repo.InsertAsync(new WarningRecord(id, Target, Actor, "duplicate", Now)));
        Assert.AreEqual("original", (await repo.GetHistoryAsync(Target)).Single().Reason);
    }

    private async Task DropTablesAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var table in new[] {
                "ano_playtime_sessions", "ano_admin_warnings", "ano_admin_action_audit", "ano_moderation_audit",
                "ano_moderation_sanctions", "ano_module_data", "ano_players",
                "ano_schema_migrations" })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE IF EXISTS {table}";
                await command.ExecuteNonQueryAsync(token);
            }

            return true;
        });
    }
}
