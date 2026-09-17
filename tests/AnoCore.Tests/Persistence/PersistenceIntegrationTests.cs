using System.Data.Common;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Composition;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Abstractions.Commands;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Persistence;

[TestClass]
[DoNotParallelize]
public sealed class PersistenceIntegrationTests
{
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
    public async Task PingAsync_ReturnsTrueForAvailableDatabase()
    {
        Assert.IsTrue(await _database.PingAsync());
    }

    [TestMethod]
    public async Task InTransactionAsync_CommitsSuccessAndRollsBackFailure()
    {
        await ExecuteAsync("CREATE TABLE ano_tx_test (id INT NOT NULL PRIMARY KEY)");

        await _database.InTransactionAsync(async (connection, transaction, cancellationToken) =>
        {
            await ExecuteAsync(connection, transaction, "INSERT INTO ano_tx_test (id) VALUES (1)", cancellationToken);
            return true;
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await _database.InTransactionAsync<bool>(async (connection, transaction, cancellationToken) =>
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO ano_tx_test (id) VALUES (2)", cancellationToken);
                throw new InvalidOperationException("force rollback");
            }));

        Assert.AreEqual(1L, await ScalarInt64Async("SELECT COUNT(*) FROM ano_tx_test"));
    }

    [TestMethod]
    public async Task MigrationRunner_AppliesOrderedMigrationsExactlyOnce()
    {
        var migrations = new IDatabaseMigration[]
        {
            new CoreSchemaMigration001(),
            new InsertOnceMigration002(),
        };
        var runner = new MigrationRunner(_database, migrations);

        var first = await runner.ApplyPendingAsync();
        var second = await runner.ApplyPendingAsync();

        Assert.AreEqual(2, first);
        Assert.AreEqual(0, second);
        Assert.AreEqual(2L, await ScalarInt64Async("SELECT COUNT(*) FROM ano_schema_migrations"));
        Assert.AreEqual(1L, await ScalarInt64Async("SELECT COUNT(*) FROM ano_migration_test"));
    }

    [TestMethod]
    public async Task PlayerRepository_UpsertsAndReadsProfile()
    {
        await new MigrationRunner(_database, [new CoreSchemaMigration001()]).ApplyPendingAsync();
        var repository = new MySqlPlayerRepository(_database);
        var id = new PlayerId(76561198000000001);
        var firstSeen = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(new PlayerProfile(id, "First", firstSeen, firstSeen));
        await repository.UpsertAsync(new PlayerProfile(id, "Renamed", firstSeen.AddDays(1), firstSeen.AddDays(2)));
        var loaded = await repository.GetAsync(id);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("Renamed", loaded.LastKnownName);
        Assert.AreEqual(firstSeen, loaded.FirstSeenUtc);
        Assert.AreEqual(firstSeen.AddDays(2), loaded.LastSeenUtc);
    }

    [TestMethod]
    public async Task PlayerRepository_StaleUpsertDoesNotRegressLatestNameOrLastSeen()
    {
        await new MigrationRunner(_database, [new CoreSchemaMigration001()]).ApplyPendingAsync();
        var repository = new MySqlPlayerRepository(_database);
        var id = new PlayerId(76561198000000002);
        var firstSeen = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var latestSeen = firstSeen.AddDays(5);

        await repository.UpsertAsync(new PlayerProfile(id, "CurrentName", firstSeen, latestSeen));
        await repository.UpsertAsync(new PlayerProfile(id, "StaleName", firstSeen.AddDays(-2), firstSeen.AddDays(1)));
        var loaded = await repository.GetAsync(id);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("CurrentName", loaded.LastKnownName);
        Assert.AreEqual(firstSeen.AddDays(-2), loaded.FirstSeenUtc);
        Assert.AreEqual(latestSeen, loaded.LastSeenUtc);
    }

    [TestMethod]
    public async Task ModuleDataStore_IsolatesValuesByModule()
    {
        await new MigrationRunner(_database, [new CoreSchemaMigration001()]).ApplyPendingAsync();
        var store = new MySqlModuleDataStore(_database);
        var veto = new ModuleId("veto");
        var stats = new ModuleId("stats");

        await store.SetAsync(veto, "shared-key", "{\"value\":1}");
        await store.SetAsync(stats, "shared-key", "{\"value\":2}");

        Assert.AreEqual("{\"value\":1}", await store.GetAsync(veto, "shared-key"));
        Assert.AreEqual("{\"value\":2}", await store.GetAsync(stats, "shared-key"));
        Assert.IsTrue(await store.DeleteAsync(veto, "shared-key"));
        Assert.IsNull(await store.GetAsync(veto, "shared-key"));
        Assert.AreEqual("{\"value\":2}", await store.GetAsync(stats, "shared-key"));
    }

    [TestMethod]
    public void MigrationRunner_RejectsDuplicateVersions()
    {
        var migrations = new IDatabaseMigration[]
        {
            new DuplicateMigration(5, "first"),
            new DuplicateMigration(5, "second"),
        };

        Assert.ThrowsExactly<ArgumentException>(() => new MigrationRunner(_database, migrations));
    }


    [TestMethod]
    public async Task RuntimeServices_PersistsProfilesAndSettingsAcrossRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "ano-runtime-" + Guid.NewGuid().ToString("N"));
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var id = new PlayerId(76561198000000009);
        var time = new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
        var key = new PlayerSettingKey<string>("language", "en");
        using (var runtime = await RuntimeServices.CreateAsync(_database, new JsonConfigStore(path), events, players))
        {
            var player = await players.ConnectAsync(new PlayerConnection(id, "Connected", PlayerTeam.Spectator, false, time));
            await runtime.Settings.SetAsync(id, key, "de");
            await players.DisconnectAsync(id, player.SessionId, time.AddMinutes(5));
            var profile = await runtime.Profiles.GetAsync(id);
            Assert.IsNotNull(profile);
            Assert.AreEqual(time, profile.FirstSeenUtc);
            Assert.AreEqual(time.AddMinutes(5), profile.LastSeenUtc);
            Assert.AreSame(runtime.Commands, runtime.GetService(typeof(IAnoCommandRegistry)));
        }

        var restartEvents = new AnoEventBus();
        using var restarted = await RuntimeServices.CreateAsync(
            _database, new JsonConfigStore(path), restartEvents, new PlayerRegistry(restartEvents));
        Assert.AreEqual("de", await restarted.Settings.GetAsync(id, key));
        Assert.AreEqual("Connected", (await restarted.Profiles.GetAsync(id))!.LastKnownName);
    }

    [TestMethod]
    public async Task RuntimeServices_BootstrapsExistingPlayersAndUnsubscribesOnDispose()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var id = new PlayerId(76561198000000010);
        var time = new DateTimeOffset(2026, 9, 17, 11, 0, 0, TimeSpan.Zero);
        var player = await players.ConnectAsync(new PlayerConnection(id, "Present", PlayerTeam.Spectator, false, time));
        var runtime = await RuntimeServices.CreateAsync(
            _database, new JsonConfigStore(Path.GetTempPath()), events, players);
        Assert.IsNotNull(await runtime.Profiles.GetAsync(id));
        runtime.Dispose();
        runtime.Dispose();
        await players.DisconnectAsync(id, player.SessionId, time.AddMinutes(5));
        Assert.AreEqual(time, (await new MySqlPlayerRepository(_database).GetAsync(id))!.LastSeenUtc);
        Assert.ThrowsExactly<ObjectDisposedException>(() => runtime.GetService(typeof(IAnoCommandRegistry)));
    }

    [TestMethod]
    public async Task RuntimeServices_CoreCommandsDenyUnassignedPlayersButAllowConsole()
    {
        var events = new AnoEventBus();
        using var runtime = await RuntimeServices.CreateAsync(
            _database, new JsonConfigStore(Path.GetTempPath()), events, new PlayerRegistry(events));
        var player = new PlayerId(76561198000000011);
        var denied = await runtime.Commands.ExecuteAsync("anoreloadauth", player);
        Assert.AreEqual(CommandFailureReason.Forbidden, denied.FailureReason);
        Assert.IsTrue((await runtime.Commands.ExecuteAsync("anoreloadauth", null)).Success);
        Assert.IsTrue((await runtime.Commands.ExecuteAsync("anocommands", player)).Success);
    }


    [TestMethod]
    public async Task RuntimeServices_RejectsCancelledStartupWithoutInstallingSubscriptions()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => RuntimeServices.CreateAsync(
            _database, new JsonConfigStore(Path.GetTempPath()), events, players, cancellation.Token));
        var id = new PlayerId(76561198000000012);
        await players.ConnectAsync(new PlayerConnection(
            id, "No persistence subscriber", PlayerTeam.Spectator, false, DateTimeOffset.UtcNow));
    }

    private async Task DropAnoTablesAsync()
    {
        await ExecuteAsync("DROP TABLE IF EXISTS ano_migration_test");
        await ExecuteAsync("DROP TABLE IF EXISTS ano_tx_test");
        await ExecuteAsync("DROP TABLE IF EXISTS ano_module_data");
        await ExecuteAsync("DROP TABLE IF EXISTS ano_players");
        await ExecuteAsync("DROP TABLE IF EXISTS ano_schema_migrations");
    }

    private Task ExecuteAsync(string sql)
        => _database.WithConnectionAsync(async (connection, cancellationToken) =>
        {
            await ExecuteAsync(connection, null, sql, cancellationToken);
            return true;
        }).AsTask();

    private ValueTask<long> ScalarInt64Async(string sql)
        => _database.WithConnectionAsync(async (connection, cancellationToken) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
        });

    private static async ValueTask ExecuteAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class InsertOnceMigration002 : IDatabaseMigration
    {
        public long Version => 2;

        public string Name => "integration-insert-once";

        public async ValueTask ApplyAsync(
            DbConnection connection,
            CancellationToken cancellationToken)
        {
            await ExecuteAsync(
                connection,
                null,
                "CREATE TABLE ano_migration_test (id INT NOT NULL PRIMARY KEY)",
                cancellationToken);
            await ExecuteAsync(
                connection,
                null,
                "INSERT INTO ano_migration_test (id) VALUES (1)",
                cancellationToken);
        }
    }

    private sealed class DuplicateMigration(long version, string name) : IDatabaseMigration
    {
        public long Version { get; } = version;

        public string Name { get; } = name;

        public ValueTask ApplyAsync(
            DbConnection connection,
            CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }
}
