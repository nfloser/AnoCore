using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Auditing;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using AnoCore.Runtime.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
[DoNotParallelize]
public sealed class MySqlStatisticsResetAdministrationServiceTests
{
    private static readonly PlayerId Target = new(76561198000190001);
    private static readonly PlayerId Other = new(76561198000190002);
    private static readonly PlayerId Actor = new(76561198000190003);
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 18, 30, 0, TimeSpan.Zero);
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
            new CombatDetailSchemaMigration009(), new GameplayStatSchemaMigration010(),
            new StatisticsResetSchemaMigration011()]).ApplyPendingAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    [TestMethod]
    public async Task Reset_HidesOnlyTargetsHistoryAndIsRestartDurable()
    {
        var combat = new MySqlCombatRepository(_database);
        var gameplay = new MySqlGameplayStatRepository(_database);
        await combat.RecordAsync(new CombatDeath(
            Guid.NewGuid(), Other, Target, null, Now));
        await combat.RecordWeaponFireAsync(new CombatWeaponFireEvent(
            Guid.NewGuid(), Target, Now.AddSeconds(1), "de_dust2", "weapon_ak47"));
        await combat.RecordDamageAsync(new CombatDamageEvent(
            Guid.NewGuid(), Other, Target, Now.AddSeconds(2),
            "de_dust2", "weapon_ak47", 1, 40, 5));
        await gameplay.RecordAsync(new GameplayStatEvent(
            Guid.NewGuid(), Target, Now.AddSeconds(3),
            "de_dust2", GameplayStatKind.Mvp));

        var service = new MySqlStatisticsResetAdministrationService(_database);
        var resetAt = Now.AddSeconds(10);
        var result = await service.ResetAsync(
            Target, Actor, "requested cleanup", resetAt);

        Assert.IsNull(result.PreviousCutoffUtc);
        Assert.AreEqual(resetAt, result.CurrentCutoffUtc);

        var restartedCombat = new MySqlCombatRepository(_database);
        var restartedGameplay = new MySqlGameplayStatRepository(_database);
        Assert.AreEqual(new CombatTotals(0, 0, 0),
            await restartedCombat.ReadAsync(Target));
        Assert.AreEqual(1L, (await restartedCombat.ReadAsync(Other)).Deaths);
        Assert.AreEqual(0L, (await restartedCombat.ReadDetailsAsync(Target)).Shots);
        Assert.AreEqual(0, (await restartedCombat.ReadHitgroupsAsync(Target)).Count);
        Assert.AreEqual(0, (await restartedGameplay.ReadAsync(Target)).Count);

        await restartedCombat.RecordAsync(new CombatDeath(
            Guid.NewGuid(), Other, Target, null, resetAt.AddSeconds(1)));
        await restartedGameplay.RecordAsync(new GameplayStatEvent(
            Guid.NewGuid(), Target, resetAt.AddSeconds(2),
            "de_dust2", GameplayStatKind.Mvp));

        Assert.AreEqual(1L, (await restartedCombat.ReadAsync(Target)).Kills);
        Assert.AreEqual(2L, (await restartedCombat.ReadAsync(Other)).Deaths);
        Assert.AreEqual(1L, (await restartedGameplay.ReadAsync(Target))
            .Single(x => x.Kind == GameplayStatKind.Mvp).Count);

        var audits = await new MySqlAdminAuditRepository(_database)
            .GetTargetHistoryAsync(Target);
        var audit = audits.Single(x => x.Id == result.AuditId);
        Assert.AreEqual("statistics.reset", audit.Action.Value);
        Assert.AreEqual(Actor, audit.ActorId);
        Assert.AreEqual("requested cleanup", audit.Reason);
    }

    [TestMethod]
    public async Task Reset_AdvancesCutoffAndRejectsStaleTimestampWithoutChangingState()
    {
        var service = new MySqlStatisticsResetAdministrationService(_database);
        var first = await service.ResetAsync(Target, Actor, "first", Now);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await service.ResetAsync(Target, Actor, "stale", Now));

        var second = await service.ResetAsync(Target, Actor, "second", Now.AddSeconds(1));
        Assert.AreEqual(first.CurrentCutoffUtc, second.PreviousCutoffUtc);
        Assert.AreEqual(Now.AddSeconds(1), second.CurrentCutoffUtc);

        var audits = await new MySqlAdminAuditRepository(_database)
            .GetTargetHistoryAsync(Target);
        Assert.AreEqual(2, audits.Count);
        Assert.IsFalse(audits.Any(x => x.Reason == "stale"));
    }

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var view in new[] {
                "ano_effective_gameplay_stats", "ano_effective_combat_damage",
                "ano_effective_combat_weapon_fire", "ano_effective_combat_assists",
                "ano_effective_combat_deaths", "ano_effective_combat_kills"
            })
            {
                await using var dropView = connection.CreateCommand();
                dropView.CommandText = $"DROP VIEW IF EXISTS {view}";
                await dropView.ExecuteNonQueryAsync(token);
            }

            foreach (var table in new[] {
                "ano_statistics_resets", "ano_gameplay_stats", "ano_combat_damage",
                "ano_combat_weapon_fire", "ano_rank_adjustments", "ano_playtime_segments",
                "ano_combat_deaths", "ano_playtime_sessions", "ano_admin_warnings",
                "ano_admin_action_audit", "ano_moderation_audit", "ano_moderation_sanctions",
                "ano_module_data", "ano_players", "ano_schema_migrations"
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
