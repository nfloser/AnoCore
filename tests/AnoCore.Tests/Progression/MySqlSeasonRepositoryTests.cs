using AnoCore.Modules.Progression;
using AnoCore.Modules.Progression.Persistence;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Tests.Progression;

[TestClass]
[DoNotParallelize]
public sealed class MySqlSeasonRepositoryTests
{
    private static readonly DateTimeOffset January =
        new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset February =
        new(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset March =
        new(2027, 3, 1, 0, 0, 0, TimeSpan.Zero);
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
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    [TestMethod]
    public async Task AcceptedCatalog_RestoresEffectiveDefinitionsAfterRestart()
    {
        var repository = new MySqlSeasonRepository(_database);
        await repository.AcceptAsync(
            new("s1", 1, "Season One", January, February), January.AddDays(-7));
        await repository.AcceptAsync(
            new("s2", 1, "Season Two", February, March), January.AddDays(-7));

        var restarted = new MySqlSeasonRepository(_database);
        var stored = await restarted.ListEffectiveAsync();
        var catalog = SeasonCatalogSnapshot.Create(
            stored.Select(value => value.Definition));

        Assert.AreEqual(2, stored.Count);
        Assert.AreEqual("s1", catalog.ResolveAt(January).Current?.Id);
        Assert.AreEqual("s2", catalog.ResolveAt(February).Current?.Id);
    }

    [TestMethod]
    public async Task SameVersion_IsIdempotentButChangedDefinitionConflicts()
    {
        var repository = new MySqlSeasonRepository(_database);
        var definition = new SeasonDefinition(
            "s1", 1, "Season One", January, February);

        var first = await repository.AcceptAsync(definition, January.AddDays(-10));
        var retry = await repository.AcceptAsync(
            definition, January.AddDays(-5));

        Assert.IsTrue(first.Inserted);
        Assert.IsFalse(retry.Inserted);
        Assert.AreEqual(first.Season, retry.Season);
        await Assert.ThrowsExactlyAsync<SeasonDefinitionConflictException>(async () =>
            await repository.AcceptAsync(
                definition with { Name = "Changed" }, January.AddDays(-5)));
    }

    [TestMethod]
    public async Task NewVersionBeforeStart_SupersedesEffectiveDefinitionAndKeepsHistory()
    {
        var repository = new MySqlSeasonRepository(_database);
        var first = new SeasonDefinition(
            "s1", 1, "Season One", January, February);
        var second = first with { Version = 2, Name = "Season One Final" };

        await repository.AcceptAsync(first, January.AddDays(-20));
        await repository.AcceptAsync(second, January.AddDays(-10));

        var effective = await repository.ListEffectiveAsync();
        Assert.AreEqual(1, effective.Count);
        Assert.AreEqual(2, effective[0].Definition.Version);
        Assert.AreEqual("Season One Final", effective[0].Definition.Name);
        Assert.IsNotNull(await repository.ReadAsync("s1", 1));
        Assert.IsNotNull(await repository.ReadAsync("s1", 2));
    }

    [TestMethod]
    public async Task NewVersionAfterSeasonStart_IsRejected()
    {
        var repository = new MySqlSeasonRepository(_database);
        var first = new SeasonDefinition(
            "s1", 1, "Season One", January, February);
        await repository.AcceptAsync(first, January.AddDays(-1));

        await Assert.ThrowsExactlyAsync<SeasonDefinitionConflictException>(async () =>
            await repository.AcceptAsync(
                first with { Version = 2, Name = "Late Revision" },
                January.AddHours(1)));
    }

    [TestMethod]
    public async Task FirstAcceptanceAtOrAfterStart_IsRejectedButStoredRetryRemainsIdempotent()
    {
        var repository = new MySqlSeasonRepository(_database);
        var definition = new SeasonDefinition(
            "s1", 1, "Season One", January, February);

        await Assert.ThrowsExactlyAsync<SeasonDefinitionConflictException>(async () =>
            await repository.AcceptAsync(definition, January));

        var first = await repository.AcceptAsync(
            definition, January.AddHours(-1));
        var retry = await repository.AcceptAsync(
            definition, January.AddHours(1));

        Assert.IsTrue(first.Inserted);
        Assert.IsFalse(retry.Inserted);
        Assert.AreEqual(first.Season, retry.Season);
    }

    [TestMethod]
    public async Task OverlappingEffectiveSeasons_AreRejected()
    {
        var repository = new MySqlSeasonRepository(_database);
        await repository.AcceptAsync(
            new("s1", 1, "Season One", January, February), January.AddDays(-10));

        var exception = await Assert.ThrowsExactlyAsync<SeasonOverlapException>(async () =>
            await repository.AcceptAsync(
                new("s2", 1, "Season Two", January.AddDays(10), March),
                January.AddDays(-10)));

        Assert.AreEqual("s2", exception.SeasonId);
        Assert.AreEqual("s1", exception.ConflictingSeasonId);
        Assert.AreEqual(1, (await repository.ListEffectiveAsync()).Count);
    }

    [TestMethod]
    public async Task Close_IsBoundaryCheckedIdempotentAndDurable()
    {
        var repository = new MySqlSeasonRepository(_database);
        await repository.AcceptAsync(
            new("s1", 1, "Season One", January, February), January.AddDays(-10));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await repository.CloseAsync("s1", 1, February.AddTicks(-1)));

        var closed = await repository.CloseAsync("s1", 1, February);
        var retry = await repository.CloseAsync("s1", 1, March);
        var restarted = await new MySqlSeasonRepository(_database).ReadAsync("s1", 1);

        Assert.AreEqual(February, closed.ClosedAtUtc);
        Assert.AreEqual(February, retry.ClosedAtUtc);
        Assert.AreEqual(February, restarted?.ClosedAtUtc);
    }

    [TestMethod]
    public async Task ConcurrentOverlappingAccepts_PersistOnlyOneEffectiveSeason()
    {
        var repository = new MySqlSeasonRepository(_database);
        async Task<Exception?> TryAccept(SeasonDefinition definition)
        {
            try
            {
                await repository.AcceptAsync(definition, January.AddDays(-10));
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var outcomes = await Task.WhenAll(
            TryAccept(new("a", 1, "A", January, February)),
            TryAccept(new("b", 1, "B", January.AddDays(5), March)));

        Assert.AreEqual(1, outcomes.Count(value => value is null));
        Assert.AreEqual(1, outcomes.Count(value => value is SeasonOverlapException));
        Assert.AreEqual(1, (await repository.ListEffectiveAsync()).Count);
    }

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
                "DROP TABLE IF EXISTS ano_progression_achievements",
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
