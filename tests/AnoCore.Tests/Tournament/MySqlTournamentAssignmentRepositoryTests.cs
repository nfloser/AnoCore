using AnoCore.Modules.Tournament;
using AnoCore.Modules.Tournament.Persistence;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Tests.Tournament;

[TestClass]
[DoNotParallelize]
public sealed class MySqlTournamentAssignmentRepositoryTests
{
    [TestMethod]
    public async Task ClaimsFenceServersTeamsAndResultsCommitExactlyOnceAcrossConnections()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured.");
        var database = new MySqlDatabase(connection!);
        await TournamentPersistenceBootstrap.EnsureReadyAsync(database);
        var definition = new TournamentEventDefinition
        {
            Id = "assigned-event",
            Name = "Assigned",
            GroupBestOf = 1,
            PlayoffsEnabled = false,
            Teams = Enumerable.Range(1, 4).Select(i => new TournamentEventTeamDefinition
            {
                Id = $"team-{i}",
                Name = $"Team {i}",
                Tag = $"T{i}",
                InitialSeed = i,
                CaptainSteamId = (ulong)(10000 + i * 10),
                Starters = Enumerable.Range(0, 5).Select(j => (ulong)(10000 + i * 10 + j)).ToList(),
            }).ToList(),
        };
        var events = new MySqlTournamentEventRepository(database);
        await events.CreateAsync(definition);
        await events.ActivateAsync(definition.Id);
        var fixtures = TournamentSchedules.Generate(definition, "main", []).First().Fixtures;
        var presence = new MySqlTournamentPresenceRepository(database);
        var instance1 = Guid.NewGuid(); var instance2 = Guid.NewGuid();
        IReadOnlyList<TournamentPlayerPresence> Players(TournamentGroupFixture fixture) => definition.Teams
            .Where(team => team.Id == fixture.HomeTeamId || team.Id == fixture.AwayTeamId).SelectMany(team => team.Starters)
            .Select(id => new TournamentPlayerPresence(id, Guid.NewGuid(), "Player", null, false)).ToArray();
        await presence.PublishAsync("assign-one", instance1, Players(fixtures[0]));
        await presence.PublishAsync("assign-two", instance2, Players(fixtures[1]));
        var config = new TournamentMatchZyConfiguration { Enabled = true, ResultBaseUrl = "https://example.com", DefaultGroupBestOf = 1 };
        var first = new MySqlTournamentAssignmentRepository(database);
        var second = new MySqlTournamentAssignmentRepository(new MySqlDatabase(connection!));
        var claims = await Task.WhenAll(first.ClaimNextAsync("assign-one", instance1, config).AsTask(), second.ClaimNextAsync("assign-two", instance2, config).AsTask());
        Assert.IsTrue(claims.All(claim => claim is not null));
        Assert.AreNotEqual(claims[0]!.Assignment.Id, claims[1]!.Assignment.Id);
        Assert.IsNull(await first.ClaimNextAsync("assign-one", instance1, config));
        await Assert.ThrowsExactlyAsync<TournamentServerIdentityConflictException>(async () => await first.ClaimNextAsync("assign-one", Guid.NewGuid(), config));
        var claim = claims[0]!;
        Assert.IsFalse(await second.AuthenticateAsync(claim.Assignment.Id, new string('0', 64)));
        Assert.IsTrue(await second.AuthenticateAsync(claim.Assignment.Id, claim.Token));
        Assert.IsTrue(await first.MarkDispatchedAsync(claim.Assignment.Id, "assign-one", instance1));
        Assert.IsFalse(await first.MarkDispatchedAsync(claim.Assignment.Id, "assign-one", instance1));
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await events.SaveAsync(definition.Id, 1, definition, []));
        var series = $$$"""{"event":"series_end","matchid":{{{claim.Assignment.Id}}},"winner":{"team":"team1"},"team1_series_score":1,"team2_series_score":0}""";
        var map = $$$"""{"event":"map_result","matchid":{{{claim.Assignment.Id}}},"map_number":0,"winner":{"team":"team1"},"team1":{"score":13},"team2":{"score":5}}""";
        Assert.IsFalse(await first.ApplyEventAsync(claim.Assignment.Id, series));
        Assert.IsTrue(await second.ApplyEventAsync(claim.Assignment.Id, map));
        Assert.IsTrue(await first.ApplyEventAsync(claim.Assignment.Id, map));
        Assert.IsTrue(await second.ApplyEventAsync(claim.Assignment.Id, series));
        var stored = (await events.LoadAsync(definition.Id))!;
        Assert.AreEqual(2L, stored.Revision);
        Assert.HasCount(1, stored.ConfirmedResults);
        Assert.AreEqual(claim.Assignment.FixtureId, stored.ConfirmedResults[0].FixtureId);
        await second.CancelAsync(claims[1]!.Assignment.Id, "assign-two", instance2);
        Assert.IsFalse(await first.AuthenticateAsync(claims[1]!.Assignment.Id, claims[1]!.Token));
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await first.ApplyEventAsync(claims[1]!.Assignment.Id, series));
    }
}
