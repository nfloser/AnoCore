using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentEventFormatTests
{
    [TestMethod]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(10)]
    public void RoundRobin_SchedulesEveryPairOnce_WithFairOddByes(int count)
    {
        var definition = CreateEvent(count);
        Assert.AreEqual(0, TournamentEventDefinition.Validate(definition).Count);

        var rounds = TournamentRoundRobin.Generate(definition, "main");

        Assert.AreEqual(count % 2 == 0 ? count - 1 : count, rounds.Count);
        Assert.AreEqual(count * (count - 1) / 2, rounds.Sum(round => round.Fixtures.Count));
        Assert.AreEqual(count % 2 == 0 ? 0 : count, rounds.Count(round => round.ByeTeamId is not null));
        var fixtures = rounds.SelectMany(round => round.Fixtures).ToArray();
        Assert.AreEqual(fixtures.Length, fixtures.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(fixtures.Length,
            fixtures.Select(f => string.Join("|", new[] { f.HomeTeamId, f.AwayTeamId }
                .OrderBy(id => id, StringComparer.Ordinal))).Distinct(StringComparer.Ordinal).Count());

        foreach (var round in rounds)
        {
            Assert.AreEqual(count, round.Fixtures.Count * 2 + (round.ByeTeamId is null ? 0 : 1));
            var ids = round.Fixtures.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId })
                .Concat(round.ByeTeamId is null ? [] : [round.ByeTeamId]);
            Assert.AreEqual(count, ids.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [TestMethod]
    public void RoundRobin_TwoLegsReverseSidesAndRetainDistinctFixtureIds()
    {
        var definition = CreateEvent(5);
        var rounds = TournamentRoundRobin.Generate(definition, "main", legs: 2);
        var fixtures = rounds.SelectMany(round => round.Fixtures).ToArray();

        Assert.AreEqual(10, rounds.Count);
        Assert.AreEqual(20, fixtures.Length);
        Assert.AreEqual(20, fixtures.Select(f => f.Id).Distinct().Count());
        foreach (var fixture in fixtures.Take(10))
        {
            Assert.IsTrue(fixtures.Any(other => other.HomeTeamId == fixture.AwayTeamId
                && other.AwayTeamId == fixture.HomeTeamId));
        }
    }

    [TestMethod]
    public void Validation_RejectsOverlappingPlayersDuplicateNamesAndBadGroupMembership()
    {
        var definition = CreateEvent(3);
        definition.Teams[1].Name = definition.Teams[0].Name.ToUpperInvariant();
        definition.Teams[1].Starters[0] = definition.Teams[0].Starters[0];
        definition.Teams[2].CaptainSteamId = 900000000;
        definition.Groups[0].TeamIds.RemoveAt(2);

        var errors = TournamentEventDefinition.Validate(definition);

        Assert.IsTrue(errors.Any(error => error.Contains("name", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(errors.Any(error => error.Contains("SteamID", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(errors.Any(error => error.Contains("captain", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(errors.Any(error => error.Contains("group", StringComparison.OrdinalIgnoreCase)));
        Assert.ThrowsExactly<ArgumentException>(() => TournamentRoundRobin.Generate(definition, "main"));
    }

    [TestMethod]
    public void Validation_RejectsTooFewTeamsAndIncorrectStarterCount()
    {
        var definition = CreateEvent(3);
        definition.Teams.RemoveAt(0);
        definition.Teams[0].Starters.RemoveAt(0);

        var errors = TournamentEventDefinition.Validate(definition);

        Assert.IsTrue(errors.Any(error => error.Contains("3", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(error => error.Contains("starters", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Standings_CalculateFootballStyleStatsAndConfigurableTiebreakers()
    {
        var definition = CreateEvent(3);
        var fixtures = TournamentRoundRobin.Generate(definition, "main")
            .SelectMany(round => round.Fixtures).ToArray();
        var results = new[]
        {
            ResultFor(fixtures, "team-1", "team-2", 2, 0, 26, 11),
            ResultFor(fixtures, "team-2", "team-3", 2, 1, 30, 25),
            ResultFor(fixtures, "team-3", "team-1", 2, 0, 26, 20),
        };

        var standings = TournamentStandings.Calculate(definition, "main", results);
        CollectionAssert.AreEqual(new[] { "team-3", "team-1", "team-2" },
            standings.Select(row => row.TeamId).ToArray());
        Assert.IsTrue(standings.All(row => row.GamesPlayed == 2 && row.Points == 3));
        Assert.AreEqual(1, standings[0].MapDifference);
        Assert.AreEqual(0, standings[1].MapDifference);
        Assert.AreEqual(-1, standings[2].MapDifference);
        Assert.AreEqual(0, standings[2].PointsBehindLeader);
    }

    [TestMethod]
    public void Standings_TracksLeaderGapAndRejectsUnconfirmedOrDuplicateGames()
    {
        var definition = CreateEvent(3);
        var fixtures = TournamentRoundRobin.Generate(definition, "main")
            .SelectMany(round => round.Fixtures).ToArray();
        var results = new[]
        {
            ResultFor(fixtures, "team-1", "team-2", 1, 0, 13, 8),
            ResultFor(fixtures, "team-1", "team-3", 1, 0, 13, 7),
            ResultFor(fixtures, "team-2", "team-3", 1, 0, 13, 9),
        };

        var standings = TournamentStandings.Calculate(definition, "main", results);
        Assert.AreEqual("team-1", standings[0].TeamId);
        Assert.AreEqual(6, standings[0].Points);
        Assert.AreEqual(3, standings[1].PointsBehindLeader);
        Assert.AreEqual(6, standings[2].PointsBehindLeader);
        Assert.ThrowsExactly<ArgumentException>(() =>
            TournamentStandings.Calculate(definition, "main", [results[0], results[0]]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            TournamentStandings.Calculate(definition, "main",
                [new TournamentGroupResult("made-up-id", 1, 0, 13, 0)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            TournamentStandings.Calculate(definition, "main",
                [new TournamentGroupResult(results[0].FixtureId, 0, 0, 0, 0)]));
    }

    [TestMethod]
    public void TwoGroups_AreIndependentAndCannotScheduleCrossGroupFixtures()
    {
        var definition = CreateEvent(6);
        definition.Groups =
        [
            new() { Id = "a", Name = "A", TeamIds = ["team-1", "team-2", "team-3"] },
            new() { Id = "b", Name = "B", TeamIds = ["team-4", "team-5", "team-6"] },
        ];

        Assert.AreEqual(3, TournamentRoundRobin.Generate(definition, "a")
            .Sum(round => round.Fixtures.Count));
        var standings = TournamentStandings.Calculate(definition, "b", []);
        Assert.AreEqual(3, standings.Count);
        Assert.IsTrue(standings.All(row => row.Points == 0 && row.GamesPlayed == 0));
    }

    private static TournamentGroupResult ResultFor(
        IReadOnlyCollection<TournamentGroupFixture> fixtures,
        string winner, string loser, int winnerMaps, int loserMaps, int winnerRounds, int loserRounds)
    {
        var fixture = fixtures.Single(f =>
            (f.HomeTeamId == winner && f.AwayTeamId == loser) ||
            (f.HomeTeamId == loser && f.AwayTeamId == winner));
        return fixture.HomeTeamId == winner
            ? new(fixture.Id, winnerMaps, loserMaps, winnerRounds, loserRounds)
            : new(fixture.Id, loserMaps, winnerMaps, loserRounds, winnerRounds);
    }

    private static TournamentEventDefinition CreateEvent(int count)
    {
        var teams = Enumerable.Range(1, count).Select(i =>
        {
            var members = Enumerable.Range(0, 5).Select(offset =>
                76561198010000000UL + (ulong)(i * 10 + offset)).ToList();
            return new TournamentEventTeamDefinition
            {
                Id = $"team-{i}",
                Name = $"Squad {i}",
                Tag = $"S{i}",
                CaptainSteamId = members[0],
                Starters = members,
                InitialSeed = i,
            };
        }).ToList();
        return new TournamentEventDefinition
        {
            Id = "test-cup",
            Name = "Test Cup",
            Teams = teams,
            Groups = [new TournamentGroupDefinition
            {
                Id = "main",
                Name = "Main group",
                TeamIds = teams.Select(t => t.Id).ToList(),
            }],
        };
    }
}
