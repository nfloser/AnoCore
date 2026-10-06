using AnoCore.Abstractions.Players;
using AnoCore.Modules.Progression;
using AnoCore.Modules.Progression.Persistence;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Tests.Progression;

[TestClass]
[DoNotParallelize]
public sealed class MySqlSeasonProgressionRepositoryTests
{
    private static readonly PlayerId Player = new(76561198000238101);
    private static readonly DateTimeOffset Start =
        new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End =
        new(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private MySqlDatabase _database = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection))
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured for integration tests.");

        _database = new MySqlDatabase(connection!);
        await DropAsync();
        await ProgressionPersistenceBootstrap.EnsureReadyAsync(_database);
        await new MySqlSeasonRepository(_database).AcceptAsync(
            new SeasonDefinition("s1", 1, "Season One", Start, End),
            Start.AddDays(-7));
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    [TestMethod]
    public async Task Grant_RetryAndRestart_AreSeasonScopedAndIdempotent()
    {
        var service = Service();
        var first = await service.GrantAsync(Player,
            new("round:1", ProgressionXpSource.Gameplay, 60, "gameplay.round", Start));

        var restarted = Service();
        var retry = await restarted.GrantAsync(Player,
            new("round:1", ProgressionXpSource.Gameplay, 60, "gameplay.round",
                Start.ToOffset(TimeSpan.FromHours(2))));
        var season = await restarted.ReadAsync(Player, "s1", 1);
        var lifetime = await new MySqlProgressionGrantRepository(_database)
            .ReadLifetimeAsync(Player);

        Assert.IsTrue(first.Applied);
        Assert.IsFalse(retry.Applied);
        Assert.AreEqual(120L, season.SeasonXp);
        Assert.AreEqual(1L, season.Revision);
        Assert.AreEqual(0L, lifetime.LifetimeXp);
        Assert.AreEqual(first.Grant, retry.Grant);
    }

    [TestMethod]
    public async Task Grant_UsesHalfOpenSeasonWindow()
    {
        var service = Service();

        var atStart = await service.GrantAsync(Player,
            new("start", ProgressionXpSource.Gameplay, 10, "gameplay.round", Start));
        Assert.IsTrue(atStart.Applied);

        await Assert.ThrowsExactlyAsync<SeasonNotActiveException>(async () =>
            await service.GrantAsync(Player,
                new("end", ProgressionXpSource.Gameplay, 10, "gameplay.round", End)));
    }

    [TestMethod]
    public async Task ClosedSeason_RejectsNewGrantButAllowsCommittedRetry()
    {
        var service = Service();
        var request = new SeasonXpGrantRequest(
            "existing", ProgressionXpSource.Gameplay, 10, "gameplay.round", Start.AddDays(1));
        var first = await service.GrantAsync(Player, request);

        await new MySqlSeasonRepository(_database).CloseAsync("s1", 1, End);

        var retry = await service.GrantAsync(Player, request);
        Assert.IsFalse(retry.Applied);
        Assert.AreEqual(first.Grant, retry.Grant);

        await Assert.ThrowsExactlyAsync<SeasonProgressionClosedException>(async () =>
            await service.GrantAsync(Player,
                new("new", ProgressionXpSource.Gameplay, 10, "gameplay.round", Start.AddDays(2))));
    }

    [TestMethod]
    public async Task AdministrativeAdjustment_CannotReduceSeasonXpBelowZero()
    {
        var service = Service();
        await service.GrantAsync(Player,
            new("earn", ProgressionXpSource.Gameplay, 10, "gameplay.round", Start));

        var adjusted = await service.GrantAsync(Player,
            new("admin:minus", ProgressionXpSource.Administration, -15, "admin.adjust", Start));
        Assert.AreEqual(5L, adjusted.Grant.SeasonXpAfter);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await service.GrantAsync(Player,
                new("admin:too-far", ProgressionXpSource.Administration, -6, "admin.adjust", Start)));

        Assert.AreEqual(
            5L,
            (await service.ReadAsync(Player, "s1", 1)).SeasonXp);
    }

    [TestMethod]
    public async Task ConcurrentSameGrant_AppliesExactlyOnce()
    {
        var service = Service();
        var request = new SeasonXpGrantRequest(
            "same", ProgressionXpSource.Gameplay, 25, "gameplay.round", Start);

        var results = await Task.WhenAll(
            service.GrantAsync(Player, request).AsTask(),
            service.GrantAsync(Player, request).AsTask());

        Assert.AreEqual(1, results.Count(value => value.Applied));
        Assert.AreEqual(1, results.Count(value => !value.Applied));
        Assert.AreEqual(50L, (await service.ReadAsync(Player, "s1", 1)).SeasonXp);
    }

    [TestMethod]
    public async Task ReadCurrent_ReturnsDerivedLevelWithoutCreatingLifetimeState()
    {
        var service = Service();
        await service.GrantAsync(Player,
            new("level", ProgressionXpSource.Gameplay, 60, "gameplay.round", Start));

        var current = await service.ReadCurrentAsync(Player, Start.AddDays(1));

        Assert.IsNotNull(current);
        Assert.AreEqual("s1", current.SeasonId);
        Assert.AreEqual(2, current.Level.Level);
        Assert.AreEqual(120L, current.SeasonXp);
    }

    private SeasonProgressionService Service()
        => new(
            new MySqlSeasonProgressionRepository(_database),
            new MySqlSeasonRepository(_database),
            ProgressionDefinitionSnapshot.Create(
                [new(1, 0), new(2, 100)],
                [new("double", Start, Start.AddDays(2), 2m)]));

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var statement in new[]
            {
                "DROP TABLE IF EXISTS ano_progression_season_grants",
                "DROP TABLE IF EXISTS ano_progression_season_accounts",
                "DROP TABLE IF EXISTS ano_progression_season_runtime",
                "DROP TABLE IF EXISTS ano_progression_seasons",
                "DROP TABLE IF EXISTS ano_progression_grants",
                "DROP TABLE IF EXISTS ano_progression_accounts",
                "DROP TABLE IF EXISTS ano_schema_migrations",
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = statement;
                await command.ExecuteNonQueryAsync(token);
            }

            return true;
        });
    }
}
