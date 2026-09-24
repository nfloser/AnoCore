using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Auditing;
using AnoCore.Runtime.Composition;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Admin;

[TestClass]
[DoNotParallelize]
public sealed class MySqlAdminAuditRepositoryTests
{
    private static readonly PlayerId Target = new(76561198000008901);
    private static readonly PlayerId Other = new(76561198000008902);
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private MySqlDatabase _database = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured for integration tests.");
        }

        _database = new MySqlDatabase(connectionString!);
        await DropTablesAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null)
        {
            await DropTablesAsync();
        }
    }

    [TestMethod]
    public async Task MigrationAndRepository_PersistAcrossRestartWithNullableParticipantsAndBoundedHistory()
    {
        var migrations = new MigrationRunner(_database,
            [new CoreSchemaMigration001(), new ModerationSchemaMigration002(), new AdminAuditSchemaMigration003()]);
        Assert.AreEqual(3, await migrations.ApplyPendingAsync());
        Assert.AreEqual(0, await migrations.ApplyPendingAsync());

        var first = new AdminAuditService(new MySqlAdminAuditRepository(_database));
        var earlier = await first.RecordAsync(new AdminActionId("kick.silent"), null, Target, "reason ' OR 1=1 --", Now);
        await first.RecordAsync(new AdminActionId("server.restart"), null, null, "maintenance", Now.AddMinutes(1));
        var later = await first.RecordAsync(new AdminActionId("warn"), Other, Target, "warning", Now.AddMinutes(2));

        var restarted = new AdminAuditService(new MySqlAdminAuditRepository(_database));
        CollectionAssert.AreEqual(new[] { earlier, later }, (await restarted.GetTargetHistoryAsync(Target)).ToArray());
        CollectionAssert.AreEqual(new[] { later }, (await restarted.GetTargetHistoryAsync(Target, 1)).ToArray());
        Assert.AreEqual(3, (await restarted.GetRecentAsync()).Count);
        Assert.AreEqual("reason ' OR 1=1 --", earlier.Reason);
        Assert.IsNull((await restarted.GetRecentAsync())[1].TargetId);
    }

    [TestMethod]
    public async Task RuntimeServices_ResolvesSharedAuditServiceAndRepository()
    {
        var events = new AnoEventBus();
        var path = Path.Combine(Path.GetTempPath(), "ano-audit-" + Guid.NewGuid().ToString("N"));
        using var runtime = await RuntimeServices.CreateAsync(
            _database, new JsonConfigStore(path), events, new PlayerRegistry(events));
        Assert.AreSame(runtime.AdminAudit, runtime.GetService(typeof(IAdminAuditService)));
        Assert.AreSame(runtime.AdminAuditRepository, runtime.GetService(typeof(IAdminAuditRepository)));
        await runtime.AdminAudit.RecordAsync(new AdminActionId("kick"), null, Target, "test", Now);
        Assert.AreEqual(1, (await runtime.AdminAudit.GetTargetHistoryAsync(Target)).Count);
    }

    private async Task DropTablesAsync()
    {
        await _database.WithConnectionAsync(async (connection, cancellationToken) =>
        {
            foreach (var table in new[] {
                "ano_admin_action_audit", "ano_moderation_audit", "ano_moderation_sanctions",
                "ano_module_data", "ano_players", "ano_schema_migrations" })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE IF EXISTS {table}";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return true;
        });
    }
}
