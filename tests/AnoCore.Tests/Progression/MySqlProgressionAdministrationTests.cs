using AnoCore.Abstractions.Players;
using AnoCore.Modules.Progression;
using AnoCore.Modules.Progression.Persistence;
using AnoCore.Runtime.Auditing;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Tests.Progression;

[TestClass]
[DoNotParallelize]
public sealed class MySqlProgressionAdministrationTests
{
    private static readonly PlayerId Player = new(76561198000293101);
    private static readonly PlayerId Actor = new(76561198000293102);
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 13, 0, 0, TimeSpan.Zero);
    private MySqlDatabase _database = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured.");
        _database = new MySqlDatabase(connection!);
        await DropAsync();
        await MySqlProgressionAdministrationService.EnsureReadyAsync(_database);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    [TestMethod]
    public async Task Operations_AuditAndKeepEarnedGrantHistoryAcrossRestart()
    {
        var grants = new MySqlProgressionGrantRepository(_database);
        var earned = await grants.ApplyAsync(Player, new("earned", ProgressionXpSource.Gameplay, 50, 50, "test", Now, null, 1m));
        var service = new MySqlProgressionAdministrationService(_database);
        Assert.AreEqual(150L, (await service.ApplyAsync(Request(ProgressionAdminOperation.Give, 100))).CurrentXp);
        Assert.AreEqual(120L, (await service.ApplyAsync(Request(ProgressionAdminOperation.Take, 30))).CurrentXp);
        Assert.AreEqual(25L, (await service.ApplyAsync(Request(ProgressionAdminOperation.Set, 25))).CurrentXp);
        Assert.AreEqual(0L, (await service.ApplyAsync(Request(ProgressionAdminOperation.Reset, 0))).CurrentXp);
        var restarted = new MySqlProgressionGrantRepository(_database);
        Assert.AreEqual(0L, (await restarted.ReadLifetimeAsync(Player)).LifetimeXp);
        Assert.AreEqual(5L, (await restarted.ReadLifetimeAsync(Player)).Revision);
        Assert.AreEqual(earned.Grant, await restarted.ReadGrantAsync(Player, "earned"));
        var audits = await new MySqlAdminAuditRepository(_database).GetTargetHistoryAsync(Player);
        Assert.HasCount(4, audits);
        Assert.IsTrue(audits.All(item => item.ActorId == Actor && item.Action.Value.StartsWith("progression.xp.", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task RetryAndConflictingRequest_OnlyApplyOnce()
    {
        var request = Request(ProgressionAdminOperation.Give, 100);
        var service = new MySqlProgressionAdministrationService(_database);
        var first = await service.ApplyAsync(request);
        var replay = await new MySqlProgressionAdministrationService(_database).ApplyAsync(
            request with { OccurredAtUtc = Now.ToOffset(TimeSpan.FromHours(2)) });
        Assert.IsTrue(first.Applied);
        Assert.IsFalse(replay.Applied);
        Assert.AreEqual(first.CurrentXp, replay.CurrentXp);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ApplyAsync(request with { Amount = 101 }).AsTask());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ApplyAsync(request with { Target = Actor }).AsTask());
        Assert.AreEqual(100L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
        Assert.HasCount(1, await new MySqlAdminAuditRepository(_database).GetTargetHistoryAsync(Player));
    }

    [TestMethod]
    public async Task ConcurrentAdministrationAndEarnedGrantsSerializeWithoutLostXp()
    {
        var service = new MySqlProgressionAdministrationService(_database);
        var grants = new MySqlProgressionGrantRepository(_database);
        var jobs = Enumerable.Range(0, 10).Select(async index =>
        {
            await service.ApplyAsync(Request(ProgressionAdminOperation.Give, 10));
            await grants.ApplyAsync(Player, new("earned:" + index, ProgressionXpSource.Gameplay, 5, 5, "test", Now, null, 1m));
        });
        await Task.WhenAll(jobs);
        var state = await grants.ReadLifetimeAsync(Player);
        Assert.AreEqual(150L, state.LifetimeXp);
        Assert.AreEqual(20L, state.Revision);
        Assert.HasCount(10, await new MySqlAdminAuditRepository(_database).GetTargetHistoryAsync(Player));
    }

    [TestMethod]
    public async Task AuditFailureRollsBackAccountAndRequestAndCanRetry()
    {
        var request = Request(ProgressionAdminOperation.Give, 100);
        var service = new MySqlProgressionAdministrationService(_database);
        await SqlAsync("CREATE TRIGGER fail_xp_admin BEFORE INSERT ON ano_admin_action_audit FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'forced audit failure'");
        var failed = false;
        try { await service.ApplyAsync(request); }
        catch (Exception) { failed = true; }
        Assert.IsTrue(failed);
        Assert.AreEqual(0L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
        await SqlAsync("DROP TRIGGER fail_xp_admin");
        Assert.IsTrue((await service.ApplyAsync(request)).Applied);
        Assert.AreEqual(100L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task BoundsAndCancellationDoNotMutateAccounts()
    {
        var service = new MySqlProgressionAdministrationService(_database);
        await service.ApplyAsync(Request(ProgressionAdminOperation.Set, 10));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.ApplyAsync(Request(ProgressionAdminOperation.Take, 11)).AsTask());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ApplyAsync(Request(ProgressionAdminOperation.Give, 1_000_000_001)).AsTask());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.ApplyAsync(Request(ProgressionAdminOperation.Give, 1), cancellation.Token).AsTask());
        Assert.AreEqual(10L, (await new MySqlProgressionGrantRepository(_database).ReadLifetimeAsync(Player)).LifetimeXp);
    }

    [TestMethod]
    public async Task MigrationAndOfflineRequestsAreRestartSafe()
    {
        await MySqlProgressionAdministrationService.EnsureReadyAsync(_database);
        var request = Request(ProgressionAdminOperation.Give, 10) with { Actor = null };
        await new MySqlProgressionAdministrationService(_database).ApplyAsync(request);
        var audit = (await new MySqlAdminAuditRepository(_database).GetTargetHistoryAsync(Player)).Single();
        Assert.IsNull(audit.ActorId);
        Assert.AreEqual(request.RequestId, audit.Id);
    }

    private static ProgressionAdminRequest Request(ProgressionAdminOperation operation, long amount)
        => new(Guid.NewGuid(), operation, Player, amount, Actor, "Correction", Now);

    private async Task SqlAsync(string sql)
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(token);
            return true;
        });
    }

    private async Task DropAsync()
    {
        foreach (var sql in new[]
        {
            "DROP TRIGGER IF EXISTS fail_xp_admin",
            "DROP TABLE IF EXISTS ano_progression_admin_requests",
            "DROP TABLE IF EXISTS ano_admin_action_audit",
            "DROP TABLE IF EXISTS ano_progression_season_grants",
            "DROP TABLE IF EXISTS ano_progression_season_accounts",
            "DROP TABLE IF EXISTS ano_progression_season_runtime",
            "DROP TABLE IF EXISTS ano_progression_seasons",
            "DROP TABLE IF EXISTS ano_progression_challenges",
            "DROP TABLE IF EXISTS ano_progression_achievements",
            "DROP TABLE IF EXISTS ano_progression_grants",
            "DROP TABLE IF EXISTS ano_progression_accounts",
            "DROP TABLE IF EXISTS ano_schema_migrations",
        }) await SqlAsync(sql);
    }
}
