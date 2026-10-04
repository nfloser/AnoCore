using System.Data.Common;
using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Composition;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Moderation;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Moderation;

[TestClass]
[DoNotParallelize]
public sealed class MySqlModerationRepositoryTests
{
    private static readonly PlayerId Target = new(76561198000002101);
    private static readonly PlayerId Admin = new(76561198000002102);
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 13, 0, 0, TimeSpan.Zero);

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
        await DropAnoTablesAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null)
        {
            await DropAnoTablesAsync();
        }
    }

    [TestMethod]
    public async Task MigrationAndRepository_RoundTripStateAcrossRepositoryInstances()
    {
        var runner = new MigrationRunner(
            _database,
            [new CoreSchemaMigration001(), new ModerationSchemaMigration002()]);

        Assert.AreEqual(2, await runner.ApplyPendingAsync());
        Assert.AreEqual(0, await runner.ApplyPendingAsync());

        var expires = Now.AddMinutes(15);
        var first = new ModerationService(new MySqlModerationRepository(_database));
        await first.ApplyAsync(
            Target,
            Admin,
            ModerationRestriction.Voice | ModerationRestriction.Chat,
            "persisted silence",
            Now,
            expires);

        var restarted = new ModerationService(new MySqlModerationRepository(_database));
        var active = await restarted.GetStateAsync(Target, expires.AddTicks(-1));
        var expired = await restarted.GetStateAsync(Target, expires);

        Assert.AreEqual(
            ModerationRestriction.Voice | ModerationRestriction.Chat,
            active.Restrictions);
        Assert.AreEqual(2, active.ActiveSanctions.Count);
        Assert.AreEqual(ModerationRestriction.None, expired.Restrictions);
        Assert.AreEqual(2, (await restarted.GetHistoryAsync(Target)).Count);
        Assert.AreEqual(1, (await restarted.GetAuditHistoryAsync(Target)).Count);
    }

    [TestMethod]
    public async Task RevokeActiveAsync_PartiallyRevokesAndPreservesHistory()
    {
        await ApplyMigrationsAsync();
        var service = new ModerationService(new MySqlModerationRepository(_database));
        await service.ApplyAsync(
            Target,
            Admin,
            ModerationRestriction.Voice | ModerationRestriction.Chat,
            "silence",
            Now);

        var revoked = await service.RevokeAsync(
            Target,
            Admin,
            ModerationRestriction.Voice,
            "voice restriction lifted",
            Now.AddMinutes(5));

        Assert.AreEqual(1, revoked.Count);
        Assert.AreEqual(ModerationRestriction.Voice, revoked.Single().Restriction);

        var state = await service.GetStateAsync(Target, Now.AddMinutes(6));
        Assert.AreEqual(ModerationRestriction.Chat, state.Restrictions);

        var history = await service.GetHistoryAsync(Target);
        Assert.AreEqual(2, history.Count);
        Assert.AreEqual(1, history.Count(value => value.RevokedAtUtc is not null));
        Assert.AreEqual(2, (await service.GetAuditHistoryAsync(Target)).Count);
    }

    [TestMethod]
    public async Task RevokeActiveAsync_AuditRecordsOnlyRestrictionsActuallyChanged()
    {
        await ApplyMigrationsAsync();
        var service = new ModerationService(new MySqlModerationRepository(_database));
        await service.ApplyAsync(
            Target,
            Admin,
            ModerationRestriction.Voice,
            "voice only",
            Now);

        var revoked = await service.RevokeAsync(
            Target,
            Admin,
            ModerationRestriction.Voice | ModerationRestriction.Chat,
            "clear communication restrictions",
            Now.AddMinutes(5));

        Assert.AreEqual(1, revoked.Count);
        var audit = await service.GetAuditHistoryAsync(Target);
        Assert.AreEqual(2, audit.Count);
        Assert.AreEqual(ModerationAuditAction.Revoked, audit[1].Action);
        Assert.AreEqual(ModerationRestriction.Voice, audit[1].Restrictions);
    }

    [TestMethod]
    public async Task RuntimeServices_ExposesPersistentModerationServicesAcrossRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "ano-moderation-" + Guid.NewGuid().ToString("N"));
        var events = new AnoEventBus();
        using (var runtime = await RuntimeServices.CreateAsync(
            _database,
            new JsonConfigStore(path),
            events,
            new PlayerRegistry(events)))
        {
            Assert.AreSame(runtime.Moderation, runtime.GetService(typeof(IModerationService)));
            Assert.AreSame(runtime.Moderation, runtime.GetService(typeof(IModerationSnapshotProvider)));
            Assert.AreSame(runtime.ModerationRepository, runtime.GetService(typeof(IModerationRepository)));

            await runtime.Moderation.ApplyAsync(
                Target,
                Admin,
                ModerationRestriction.Connect,
                "runtime persisted ban",
                Now);
        }

        var restartEvents = new AnoEventBus();
        using var restarted = await RuntimeServices.CreateAsync(
            _database,
            new JsonConfigStore(path),
            restartEvents,
            new PlayerRegistry(restartEvents));

        Assert.AreEqual(
            ModerationRestriction.Connect,
            (await restarted.Moderation.GetStateAsync(Target, Now.AddMinutes(1))).Restrictions);
    }

    [TestMethod]
    public async Task GetStateAsync_HistoricalQueryBeforeLaterRevocationRemainsActive()
    {
        await ApplyMigrationsAsync();
        var service = new ModerationService(new MySqlModerationRepository(_database));
        await service.ApplyAsync(
            Target,
            Admin,
            ModerationRestriction.Chat,
            "historical gag",
            Now);

        await service.RevokeAsync(
            Target,
            Admin,
            ModerationRestriction.Chat,
            "later revoke",
            Now.AddMinutes(10));

        Assert.AreEqual(
            ModerationRestriction.Chat,
            (await service.GetStateAsync(Target, Now.AddMinutes(5))).Restrictions);
        Assert.AreEqual(
            ModerationRestriction.None,
            (await service.GetStateAsync(Target, Now.AddMinutes(10))).Restrictions);
    }

    [TestMethod]
    public async Task AddAsync_RejectsAuditMetadataThatDoesNotMatchSanctions()
    {
        await ApplyMigrationsAsync();
        var repository = new MySqlModerationRepository(_database);
        var sanction = Sanction(Guid.NewGuid(), ModerationRestriction.Chat, "matched reason");
        var mismatchedAudit = new ModerationAuditEntry(
            Guid.NewGuid(),
            Target,
            null,
            ModerationAuditAction.Applied,
            ModerationRestriction.Chat,
            "matched reason",
            Now);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await repository.AddAsync([sanction], mismatchedAudit));

        Assert.AreEqual(0, (await repository.GetHistoryAsync(Target)).Count);
        Assert.AreEqual(0, (await repository.GetAuditHistoryAsync(Target)).Count);
    }

    [TestMethod]
    public async Task RevokeActiveAsync_RejectsAuditMetadataThatDoesNotMatchOperation()
    {
        await ApplyMigrationsAsync();
        var repository = new MySqlModerationRepository(_database);
        var sanction = Sanction(Guid.NewGuid(), ModerationRestriction.Connect, "ban");
        await repository.AddAsync(
            [sanction],
            Audit(Guid.NewGuid(), ModerationAuditAction.Applied, ModerationRestriction.Connect, "ban"));

        var mismatchedAudit = new ModerationAuditEntry(
            Guid.NewGuid(),
            Target,
            null,
            ModerationAuditAction.Revoked,
            ModerationRestriction.Connect,
            "different reason",
            Now.AddMinutes(5));

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await repository.RevokeActiveAsync(
                Target,
                ModerationRestriction.Connect,
                Admin,
                "expected reason",
                Now.AddMinutes(5),
                mismatchedAudit));

        Assert.AreEqual(
            ModerationRestriction.Connect,
            (await new ModerationService(repository).GetStateAsync(Target, Now.AddMinutes(6))).Restrictions);
        Assert.AreEqual(1, (await repository.GetAuditHistoryAsync(Target)).Count);
    }

    [TestMethod]
    public async Task AddAsync_RollsBackSanctionsWhenAuditInsertFails()
    {
        await ApplyMigrationsAsync();
        var repository = new MySqlModerationRepository(_database);
        var auditId = Guid.NewGuid();

        var firstSanction = Sanction(Guid.NewGuid(), ModerationRestriction.Chat, "first");
        var firstAudit = Audit(auditId, ModerationAuditAction.Applied, ModerationRestriction.Chat, "first");
        await repository.AddAsync([firstSanction], firstAudit);

        var secondSanction = Sanction(Guid.NewGuid(), ModerationRestriction.Voice, "must roll back");
        var duplicateAudit = Audit(
            auditId,
            ModerationAuditAction.Applied,
            ModerationRestriction.Voice,
            "must roll back");

        await Assert.ThrowsAsync<DbException>(async () =>
            await repository.AddAsync([secondSanction], duplicateAudit));

        var history = await repository.GetHistoryAsync(Target);
        Assert.AreEqual(1, history.Count);
        Assert.AreEqual(firstSanction.Id, history.Single().Id);
    }

    [TestMethod]
    public async Task RevokeActiveAsync_RollsBackRevocationWhenAuditInsertFails()
    {
        await ApplyMigrationsAsync();
        var repository = new MySqlModerationRepository(_database);
        var auditId = Guid.NewGuid();
        var sanction = Sanction(Guid.NewGuid(), ModerationRestriction.Connect, "ban");
        var appliedAudit = Audit(auditId, ModerationAuditAction.Applied, ModerationRestriction.Connect, "ban");
        await repository.AddAsync([sanction], appliedAudit);

        var duplicateAudit = new ModerationAuditEntry(
            auditId,
            Target,
            Admin,
            ModerationAuditAction.Revoked,
            ModerationRestriction.Connect,
            "must roll back",
            Now.AddMinutes(5));
        await Assert.ThrowsAsync<DbException>(async () =>
            await repository.RevokeActiveAsync(
                Target,
                ModerationRestriction.Connect,
                Admin,
                "must roll back",
                Now.AddMinutes(5),
                duplicateAudit));

        var history = await repository.GetHistoryAsync(Target);
        Assert.IsNull(history.Single().RevokedAtUtc);
        Assert.AreEqual(
            ModerationRestriction.Connect,
            (await new ModerationService(repository).GetStateAsync(Target, Now.AddMinutes(6))).Restrictions);
        Assert.AreEqual(1, (await repository.GetAuditHistoryAsync(Target)).Count);
    }

    private ValueTask<int> ApplyMigrationsAsync()
        => new MigrationRunner(
            _database,
            [new CoreSchemaMigration001(), new ModerationSchemaMigration002()])
            .ApplyPendingAsync();

    private static ModerationSanction Sanction(
        Guid id,
        ModerationRestriction restriction,
        string reason)
        => new(
            id,
            Target,
            Admin,
            restriction,
            reason,
            Now);

    private static ModerationAuditEntry Audit(
        Guid id,
        ModerationAuditAction action,
        ModerationRestriction restrictions,
        string reason)
        => new(
            id,
            Target,
            Admin,
            action,
            restrictions,
            reason,
            Now);

    private async Task DropAnoTablesAsync()
    {
        await _database.WithConnectionAsync(async (connection, cancellationToken) =>
        {
            foreach (var table in new[]
            {
                "ano_gameplay_stats", "ano_combat_damage", "ano_combat_weapon_fire", "ano_playtime_segments", "ano_combat_deaths",
                "ano_playtime_sessions", "ano_admin_warnings",
                "ano_admin_action_audit",
                "ano_moderation_audit",
                "ano_moderation_sanctions",
                "ano_module_data",
                "ano_players",
                "ano_schema_migrations",
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE IF EXISTS {table}";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return true;
        });
    }
}
