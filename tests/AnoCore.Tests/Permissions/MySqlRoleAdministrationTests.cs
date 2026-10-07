using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Auditing;
using AnoCore.Runtime.Permissions;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Tests.Permissions;

[TestClass]
[DoNotParallelize]
public sealed class MySqlRoleAdministrationTests
{
    private MySqlDatabase _database = null!;
    private ModuleDataAuthorizationStore _store = null!;
    private AuthorizationService _authorization = null!;
    private static readonly PlayerId First = new(76561198000214301);
    private static readonly PlayerId Second = new(76561198000214302);

    [TestInitialize]
    public async Task Initialize()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("MariaDB is not configured.");
        _database = new(connection!);
        await new MigrationRunner(_database, [new CoreSchemaMigration001(), new AdminAuditSchemaMigration003()]).ApplyPendingAsync();
        _store = new(new MySqlModuleDataStore(_database));
        await _store.SaveAsync(AuthorizationState.Empty);
        _authorization = new(_store);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_database is null) return;
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM ano_admin_action_audit WHERE action_id LIKE 'roles.%'; DELETE FROM ano_module_data WHERE module_id = 'authorization'";
            await command.ExecuteNonQueryAsync(token);
            return true;
        });
    }

    [TestMethod]
    public async Task ConcurrentGrantsPersistBothTargetsAndAuditAndSurviveRestart()
    {
        var service = new MySqlRoleAdministration(_database, _authorization);
        await service.DefineAsync(null, new(new("vip"), 10, [], [new("ano.stats.*", PermissionEffect.Allow)], ["vip"]));
        await Task.WhenAll(service.ChangeAsync(null, First, new("vip"), 5, false, "first").AsTask(),
            service.ChangeAsync(null, Second, new("vip"), 0, false, "second").AsTask());
        var restarted = new AuthorizationService(_store);
        await restarted.ReloadAsync();
        Assert.IsTrue(await restarted.HasTagAsync(First, "vip"));
        Assert.IsTrue(await restarted.HasTagAsync(Second, "vip"));
        var audit = new MySqlAdminAuditRepository(_database);
        Assert.HasCount(1, await audit.GetTargetHistoryAsync(First));
        await service.ChangeAsync(null, First, new("vip"), 0, true, "remove");
        Assert.IsFalse(await _authorization.HasTagAsync(First, "vip"));
        Assert.IsTrue(await _authorization.HasTagAsync(Second, "vip"));
    }

    [TestMethod]
    public async Task AuditFailureRollsBackGrantAndPreservesActiveSnapshot()
    {
        var id = Guid.NewGuid();
        var service = new MySqlRoleAdministration(_database, _authorization, newAuditId: () => id);
        await service.DefineAsync(null, new(new("vip"), 1, [], [], ["vip"]));
        await Assert.ThrowsAsync<Exception>(async () => await service.ChangeAsync(null, First, new("vip"), 0, false, "duplicate audit"));
        Assert.IsFalse(await _authorization.HasTagAsync(First, "vip"));
        Assert.IsEmpty((await _store.LoadAsync())!.Players);
    }
}
