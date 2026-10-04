using System.Data.Common;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
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
    public async Task StateSegments_TransitionAtomicallyAndClipAcrossUtcMidnight()
    {
        await new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004(),
            new PlaytimeSchemaMigration005(), new CombatSchemaMigration006(),
            new RankAdjustmentSchemaMigration007(), new PlaytimeStateSchemaMigration008()])
            .ApplyPendingAsync();

        var session = PlayerSessionId.New();
        var repository = new MySqlPlaytimeRepository(_database);
        await repository.OpenStateAsync(Player, session, Start,
            new PlaytimeState(PlayerTeam.Terrorist, true));
        await repository.AdvanceStateAsync(Player, session, Start.AddSeconds(45),
            new PlaytimeState(PlayerTeam.CounterTerrorist, true));
        await repository.AdvanceStateAsync(Player, session, Start.AddSeconds(75),
            new PlaytimeState(PlayerTeam.CounterTerrorist, false));
        await repository.AdvanceStateAsync(Player, session, Start.AddSeconds(60),
            new PlaytimeState(PlayerTeam.Terrorist, false));
        await repository.AdvanceStateAsync(Player, session, Start.AddSeconds(105),
            new PlaytimeState(PlayerTeam.CounterTerrorist, false), close: true);
        await repository.AdvanceStateAsync(Player, session, Start.AddHours(2),
            new PlaytimeState(PlayerTeam.Terrorist, true));

        var restarted = new MySqlPlaytimeRepository(_database);
        var totals = await restarted.ReadAsync(Player, new DateOnly(2026, 9, 26));
        Assert.AreEqual(TimeSpan.FromSeconds(105), totals.Total);
        Assert.AreEqual(TimeSpan.FromSeconds(75), totals.Today);

        var breakdown = await restarted.ReadStateBreakdownAsync(
            Player, new DateOnly(2026, 9, 26));
        var terroristAlive = breakdown.Single(entry =>
            entry.Team == PlayerTeam.Terrorist && entry.IsAlive);
        var ctAlive = breakdown.Single(entry =>
            entry.Team == PlayerTeam.CounterTerrorist && entry.IsAlive);
        var ctDead = breakdown.Single(entry =>
            entry.Team == PlayerTeam.CounterTerrorist && !entry.IsAlive);

        Assert.AreEqual(TimeSpan.FromSeconds(45), terroristAlive.Total);
        Assert.AreEqual(TimeSpan.FromSeconds(15), terroristAlive.Today);
        Assert.AreEqual(TimeSpan.FromSeconds(30), ctAlive.Total);
        Assert.AreEqual(TimeSpan.FromSeconds(30), ctAlive.Today);
        Assert.AreEqual(TimeSpan.FromSeconds(30), ctDead.Total);
        Assert.AreEqual(TimeSpan.FromSeconds(30), ctDead.Today);
    }

    [TestMethod]
    public async Task DelayedStateTransition_SplitsAlreadyAccountedOpenSegment()
    {
        await new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004(),
            new PlaytimeSchemaMigration005(), new CombatSchemaMigration006(),
            new RankAdjustmentSchemaMigration007(), new PlaytimeStateSchemaMigration008()])
            .ApplyPendingAsync();

        var session = PlayerSessionId.New();
        var repository = new MySqlPlaytimeRepository(_database);
        await repository.OpenStateAsync(Player, session, Start,
            new PlaytimeState(PlayerTeam.Terrorist, true));
        await repository.AdvanceStateAsync(Player, session, Start.AddSeconds(30),
            new PlaytimeState(PlayerTeam.Terrorist, true));
        await repository.AdvanceStateAsync(Player, session, Start.AddSeconds(20),
            new PlaytimeState(PlayerTeam.CounterTerrorist, true));
        await repository.AdvanceStateAsync(Player, session, Start.AddSeconds(40),
            new PlaytimeState(PlayerTeam.CounterTerrorist, true), close: true);

        var totals = await repository.ReadAsync(Player, new DateOnly(2026, 9, 26));
        Assert.AreEqual(TimeSpan.FromSeconds(40), totals.Total);

        var breakdown = await repository.ReadStateBreakdownAsync(
            Player, new DateOnly(2026, 9, 26));
        Assert.AreEqual(TimeSpan.FromSeconds(20), breakdown.Single(entry =>
            entry.Team == PlayerTeam.Terrorist && entry.IsAlive).Total);
        Assert.AreEqual(TimeSpan.FromSeconds(20), breakdown.Single(entry =>
            entry.Team == PlayerTeam.CounterTerrorist && entry.IsAlive).Total);
    }

    [TestMethod]
    public async Task StaleDisconnect_ClosesAtCurrentCheckpointAndRejectsLaterAdvance()
    {
        await new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004(),
            new PlaytimeSchemaMigration005(), new CombatSchemaMigration006(),
            new RankAdjustmentSchemaMigration007(), new PlaytimeStateSchemaMigration008()])
            .ApplyPendingAsync();

        var session = PlayerSessionId.New();
        var repository = new MySqlPlaytimeRepository(_database);
        await repository.OpenStateAsync(Player, session, Start,
            new PlaytimeState(PlayerTeam.Terrorist, true));
        await repository.AdvanceStateAsync(Player, session, Start.AddSeconds(60),
            new PlaytimeState(PlayerTeam.Terrorist, true));
        await repository.AdvanceStateAsync(Player, session, Start.AddSeconds(30),
            new PlaytimeState(PlayerTeam.Terrorist, false), close: true);
        await repository.AdvanceStateAsync(Player, session, Start.AddSeconds(120),
            new PlaytimeState(PlayerTeam.CounterTerrorist, true));

        var totals = await repository.ReadAsync(Player, new DateOnly(2026, 9, 26));
        Assert.AreEqual(TimeSpan.FromSeconds(60), totals.Total);
        var breakdown = await repository.ReadStateBreakdownAsync(
            Player, new DateOnly(2026, 9, 26));
        Assert.AreEqual(TimeSpan.FromSeconds(60), breakdown.Single().Total);
        Assert.AreEqual(PlayerTeam.Terrorist, breakdown.Single().Team);
        Assert.IsTrue(breakdown.Single().IsAlive);
    }

    [TestMethod]
    public async Task StateSegments_PreserveLegacyTotalsAndSessionOwnership()
    {
        await new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004(),
            new PlaytimeSchemaMigration005(), new CombatSchemaMigration006(),
            new RankAdjustmentSchemaMigration007(), new PlaytimeStateSchemaMigration008()])
            .ApplyPendingAsync();

        var repository = new MySqlPlaytimeRepository(_database);
        var legacy = PlayerSessionId.New();
        await repository.OpenAsync(Player, legacy, Start);
        await repository.AdvanceAsync(Player, legacy, Start.AddSeconds(30), close: true);

        var stateSession = PlayerSessionId.New();
        await repository.OpenStateAsync(Player, stateSession, Start.AddMinutes(1),
            new PlaytimeState(PlayerTeam.CounterTerrorist, true));
        await repository.AdvanceStateAsync(Other, stateSession, Start.AddMinutes(3),
            new PlaytimeState(PlayerTeam.Terrorist, false), close: true);
        await repository.AdvanceStateAsync(Player, stateSession, Start.AddMinutes(2),
            new PlaytimeState(PlayerTeam.CounterTerrorist, true), close: true);

        var totals = await repository.ReadAsync(Player, new DateOnly(2026, 9, 26));
        Assert.AreEqual(TimeSpan.FromSeconds(90), totals.Total);
        var breakdown = await repository.ReadStateBreakdownAsync(
            Player, new DateOnly(2026, 9, 26));
        Assert.AreEqual(1, breakdown.Count);
        Assert.AreEqual(TimeSpan.FromMinutes(1), breakdown.Single().Total);
        Assert.AreEqual(PlayerTeam.CounterTerrorist, breakdown.Single().Team);
    }

    [TestMethod]
    public async Task ConcurrentStateCheckpoints_KeepMonotonicMaximum()
    {
        await new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004(),
            new PlaytimeSchemaMigration005(), new CombatSchemaMigration006(),
            new RankAdjustmentSchemaMigration007(), new PlaytimeStateSchemaMigration008()])
            .ApplyPendingAsync();

        var session = PlayerSessionId.New();
        var repository = new MySqlPlaytimeRepository(_database);
        await repository.OpenStateAsync(Player, session, Start,
            new PlaytimeState(PlayerTeam.Terrorist, true));

        var checkpoints = Enumerable.Range(1, 20)
            .Select(index => repository.AdvanceStateAsync(
                Player, session, Start.AddSeconds(index * 5),
                new PlaytimeState(PlayerTeam.Terrorist, true)).AsTask())
            .ToArray();
        await Task.WhenAll(checkpoints);

        var totals = await repository.ReadAsync(Player, new DateOnly(2026, 9, 26));
        Assert.AreEqual(TimeSpan.FromSeconds(100), totals.Total);
        var breakdown = await repository.ReadStateBreakdownAsync(
            Player, new DateOnly(2026, 9, 26));
        Assert.AreEqual(TimeSpan.FromSeconds(100), breakdown.Single().Total);
    }

    [TestMethod]
    public async Task FailedStateTransition_RollsBackSessionAndSegmentTogether()
    {
        await new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004(),
            new PlaytimeSchemaMigration005(), new CombatSchemaMigration006(),
            new RankAdjustmentSchemaMigration007(), new PlaytimeStateSchemaMigration008()])
            .ApplyPendingAsync();

        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "ALTER TABLE ano_playtime_segments ADD CONSTRAINT chk_test_team CHECK (team <= 3)";
            await command.ExecuteNonQueryAsync(token);
            return true;
        });

        var session = PlayerSessionId.New();
        var repository = new MySqlPlaytimeRepository(_database);
        await repository.OpenStateAsync(Player, session, Start,
            new PlaytimeState(PlayerTeam.Terrorist, true));
        await repository.AdvanceStateAsync(Player, session, Start.AddSeconds(10),
            new PlaytimeState(PlayerTeam.Terrorist, true));

        DbException? failure = null;
        try
        {
            await repository.AdvanceStateAsync(Player, session, Start.AddSeconds(20),
                new PlaytimeState((PlayerTeam)99, false));
        }
        catch (DbException exception)
        {
            failure = exception;
        }

        Assert.IsNotNull(failure);
        var totals = await repository.ReadAsync(Player, new DateOnly(2026, 9, 26));
        Assert.AreEqual(TimeSpan.FromSeconds(10), totals.Total);
        var breakdown = await repository.ReadStateBreakdownAsync(
            Player, new DateOnly(2026, 9, 26));
        Assert.AreEqual(1, breakdown.Count);
        Assert.AreEqual(TimeSpan.FromSeconds(10), breakdown.Single().Total);
        Assert.AreEqual(PlayerTeam.Terrorist, breakdown.Single().Team);
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

        await new MySqlPlayerRepository(_database).UpsertAsync(
            new PlayerProfile(leader, "Lead | player", Start, Start.AddMinutes(2)));
        var restarted = new MySqlPlaytimeRepository(_database);
        var firstPage = await restarted.GetTopAsync(2, 0);
        var secondPage = await restarted.GetTopAsync(2, 2);
        CollectionAssert.AreEqual(new[] { leader, low },
            firstPage.Select(entry => entry.PlayerId).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, firstPage.Select(entry => entry.Position).ToArray());
        Assert.AreEqual(TimeSpan.FromMinutes(2), firstPage[0].Total);
        Assert.AreEqual("Lead | player", firstPage[0].DisplayName);
        Assert.IsNull(firstPage[1].DisplayName);
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
                "ano_playtime_segments", "ano_combat_deaths", "ano_playtime_sessions", "ano_admin_warnings", "ano_admin_action_audit",
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
