using AnoCore.Modules.Tournament;
using AnoCore.Modules.Tournament.Persistence;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Tests.Tournament;

[TestClass]
[DoNotParallelize]
public sealed class MySqlTournamentEventRepositoryTests
{
    private MySqlDatabase _database = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured.");
        _database = new MySqlDatabase(connectionString!);
        await TournamentPersistenceBootstrap.EnsureReadyAsync(_database);
    }

    [TestMethod]
    public async Task SharedEvent_AppearsToIndependentConnectionsAndSurvivesReload()
    {
        var definition = Definition("shared-event-reload");
        var writer = new MySqlTournamentEventRepository(_database);
        var reader = new MySqlTournamentEventRepository(new MySqlDatabase(
            Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL")!));

        var created = await writer.CreateAsync(definition);
        Assert.AreEqual(1L, created.Revision);
        await writer.ActivateAsync(definition.Id);

        var active = await reader.LoadActiveAsync();
        Assert.IsNotNull(active);
        Assert.AreEqual(definition.Id, active.Definition.Id);
        Assert.AreEqual(3, active.Definition.Teams.Count);

        var fixture = TournamentRoundRobin.Generate(definition, "main")
            .SelectMany(round => round.Fixtures).First();
        var saved = await writer.SaveAsync(definition.Id, created.Revision, definition,
            [new TournamentGroupResult(fixture.Id, 2, 0, 26, 12)]);
        var refreshed = await reader.LoadActiveAsync();
        Assert.IsNotNull(refreshed);
        Assert.AreEqual(2L, saved.Revision);
        Assert.AreEqual(2L, refreshed.Revision);
        Assert.AreEqual(fixture.Id, refreshed.ConfirmedResults.Single().FixtureId);
        Assert.AreEqual(3, TournamentStandings.Calculate(refreshed.Definition, "main",
            refreshed.ConfirmedResults).Count);
    }

    [TestMethod]
    public async Task StaleServer_CannotOverwriteResultsFromAnotherServer()
    {
        var definition = Definition("shared-event-concurrency");
        var first = new MySqlTournamentEventRepository(_database);
        var second = new MySqlTournamentEventRepository(_database);
        await first.CreateAsync(definition);
        var fixture = TournamentRoundRobin.Generate(definition, "main")
            .SelectMany(round => round.Fixtures).First();
        var recorded = new TournamentGroupResult(fixture.Id, 1, 0, 13, 6);
        var applied = await first.SaveAsync(definition.Id, 1, definition, [recorded]);
        Assert.AreEqual(2L, applied.Revision);
        await Assert.ThrowsExactlyAsync<TournamentEventConcurrencyException>(async () =>
            await second.SaveAsync(definition.Id, 1, definition, []));
        var persisted = await second.LoadAsync(definition.Id);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(2L, persisted.Revision);
        Assert.AreEqual(1, persisted.ConfirmedResults.Count);
    }

    [TestMethod]
    public async Task InvalidOrDuplicateFixturesDoNotOverwriteExistingSnapshot()
    {
        var definition = Definition("shared-event-validation");
        var repository = new MySqlTournamentEventRepository(_database);
        await repository.CreateAsync(definition);
        var fixture = TournamentRoundRobin.Generate(definition, "main")
            .SelectMany(round => round.Fixtures).First();
        var result = new TournamentGroupResult(fixture.Id, 1, 0, 13, 4);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await repository.SaveAsync(definition.Id, 1, definition, [result, result]));
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await repository.SaveAsync(definition.Id, 1, definition,
                [new TournamentGroupResult("unknown-fixture", 1, 0, 13, 4)]));
        Assert.AreEqual(0, (await repository.LoadAsync(definition.Id))!.ConfirmedResults.Count);
        Assert.AreEqual(1L, (await repository.LoadAsync(definition.Id))!.Revision);
    }

    [TestMethod]
    public async Task Activate_RejectsUnknownEventWithoutWipingActivePointer()
    {
        var definition = Definition("shared-event-active");
        var repository = new MySqlTournamentEventRepository(_database);
        await repository.CreateAsync(definition);
        await repository.ActivateAsync(definition.Id);
        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(async () =>
            await repository.ActivateAsync("unknown-event"));
        Assert.AreEqual(definition.Id, (await repository.LoadActiveAsync())!.Definition.Id);
    }

    [TestMethod]
    public async Task PlayoffsShareRevisionAndSurviveGroupWritesAndRestart()
    {
        var definition = Definition("shared-playoff-storage");
        var writer = new MySqlTournamentEventRepository(_database);
        await writer.CreateAsync(definition);
        var entries = TournamentEventPlayoffs.Entrants(definition);
        var opening = TournamentBracketProgression.Calculate(entries, []).Matches.Single(match => match.IsReady);
        var result = new TournamentBracketResult(opening.Id, opening.HomeTeamId!, 1, 0);
        var saved = await writer.SavePlayoffsAsync(definition.Id, 1, [result]);
        Assert.AreEqual(2L, saved.Revision);
        var reader = new MySqlTournamentEventRepository(new MySqlDatabase(Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL")!));
        var loaded = (await reader.LoadAsync(definition.Id))!;
        Assert.HasCount(1, loaded.PlayoffResults);
        Assert.IsTrue(TournamentBracketProgression.Calculate(entries, loaded.PlayoffResults).Matches.Last().IsReady);
        await Assert.ThrowsExactlyAsync<TournamentEventConcurrencyException>(async () =>
            await reader.SaveAsync(definition.Id, 1, definition, []));
        var updated = await reader.SaveAsync(definition.Id, 2, definition, []);
        Assert.HasCount(1, updated.PlayoffResults);
        Assert.HasCount(1, (await writer.LoadAsync(definition.Id))!.PlayoffResults);
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await writer.SavePlayoffsAsync(definition.Id, 3, [result, result]));
        Assert.AreEqual(3L, (await reader.LoadAsync(definition.Id))!.Revision);
    }

    private static TournamentEventDefinition Definition(string id)
    {
        var teams = Enumerable.Range(1, 3).Select(i =>
        {
            var starters = Enumerable.Range(0, 5).Select(j => 76561198020000000UL +
                (ulong)(i * 10 + j)).ToList();
            return new TournamentEventTeamDefinition
            {
                Id = $"team-{i}",
                Name = $"Squad {i}",
                Tag = $"T{i}",
                CaptainSteamId = starters[0],
                Starters = starters,
                InitialSeed = i,
            };
        }).ToList();
        return new TournamentEventDefinition
        {
            Id = id,
            Name = "Shared event",
            Teams = teams,
            Groups =
            [
                new TournamentGroupDefinition
                {
                    Id = "main",
                    Name = "Main",
                    TeamIds = teams.Select(team => team.Id).ToList(),
                },
            ],
        };
    }
}
