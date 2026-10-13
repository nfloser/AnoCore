using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentAssignmentPlanParityTests
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
    public void AssignmentAndPreviewAgreeForAllSupportedRoundRobinSizes(int count)
    {
        var definition = Definition(count);
        var config = Configuration();
        Compare(new(definition, [], 1), config);
        var fixture = TournamentSchedules.Generate(definition, "main", [])
            .SelectMany(round => round.Fixtures).First();
        Compare(new(definition, [new(fixture.Id, 2, 0, 26, 11)], 2), config);
    }

    [TestMethod]
    public void SwissDoesNotScheduleFutureRoundsUntilPreviousRoundFinishes()
    {
        var definition = Definition(5);
        definition.GroupFormat = TournamentGroupFormat.Swiss;
        definition.SwissRounds = 2;
        definition.PlayoffsEnabled = false;
        var config = Configuration();
        var initial = new TournamentStoredEvent(definition, [], 1);
        Compare(initial, config);
        Assert.AreEqual(2, TournamentMatchAssignmentPlan.Ready(initial, config).Count);

        var opening = TournamentSchedules.Generate(definition, "main", [])
            .SelectMany(round => round.Fixtures).ToArray();
        var partial = new TournamentStoredEvent(definition, [new(opening[0].Id, 2, 0, 26, 10)], 2);
        Compare(partial, config);
        Assert.AreEqual(1, TournamentMatchAssignmentPlan.Ready(partial, config).Count);

        var complete = new TournamentStoredEvent(definition,
            [new(opening[0].Id, 2, 0, 26, 10), new(opening[1].Id, 2, 0, 26, 11)], 3);
        Compare(complete, config);
        var next = TournamentMatchAssignmentPlan.Ready(complete, config);
        Assert.AreEqual(2, next.Count);
        Assert.IsTrue(next.All(item => !opening.Any(previous => previous.Id == item.Id)));
    }

    [TestMethod]
    public void DisabledGroupStageAndDoubleEliminationYieldOnlyResolvablePlayoffMatches()
    {
        var definition = Definition(4);
        definition.GroupStageEnabled = false;
        definition.PlayoffFormat = TournamentPlayoffFormat.DoubleElimination;
        var config = Configuration();
        var initial = new TournamentStoredEvent(definition, [], 1);
        Compare(initial, config);
        var opening = TournamentMatchAssignmentPlan.Ready(initial, config);
        Assert.AreEqual(2, opening.Count);
        Assert.IsTrue(opening.All(item => item.IsPlayoff && item.Id.StartsWith("u-", StringComparison.Ordinal)));

        var winner = opening[0].HomeTeamId;
        var after = new TournamentStoredEvent(definition, [], 2)
        {
            PlayoffResults = [new(opening[0].Id, winner, 2, 0)],
        };
        Compare(after, config);
        Assert.IsFalse(TournamentMatchAssignmentPlan.Ready(after, config)
            .Any(item => item.Id == opening[0].Id));
    }

    [TestMethod]
    public void QualificationAndConfiguredBestOfOverridesRemainConsistent()
    {
        var definition = Definition(3);
        definition.PlayoffQualifiersPerGroup = 2;
        definition.GroupBestOf = 1;
        definition.PlayoffBestOf = 5;
        var config = Configuration();
        var initial = new TournamentStoredEvent(definition, [], 1);
        Compare(initial, config);
        Assert.IsTrue(TournamentMatchAssignmentPlan.Ready(initial, config)
            .All(item => !item.IsPlayoff && item.BestOf == 1));

        var completed = TournamentRoundRobin.Generate(definition, "main")
            .SelectMany(round => round.Fixtures)
            .Select(fixture => new TournamentGroupResult(fixture.Id, 1, 0, 13, 7))
            .ToArray();
        var eventState = new TournamentStoredEvent(definition, completed, 2);
        Compare(eventState, config);
        var playoff = TournamentMatchAssignmentPlan.Ready(eventState, config);
        Assert.AreEqual(1, playoff.Count);
        Assert.IsTrue(playoff[0].IsPlayoff);
        Assert.AreEqual(5, playoff[0].BestOf);
    }

    [TestMethod]
    public void MatchZyDefaultsApplyWithoutChangingTheImportedEvent()
    {
        var definition = Definition(3);
        definition.PlayoffsEnabled = false;
        Assert.IsNull(definition.GroupBestOf);
        var config = Configuration();
        var state = new TournamentStoredEvent(definition, [], 1);
        var matches = TournamentMatchAssignmentPlan.Ready(state, config);
        Assert.IsTrue(matches.All(item => item.BestOf == 3));
        Assert.IsNull(definition.GroupBestOf);
    }

    private static void Compare(TournamentStoredEvent stored, TournamentMatchZyConfiguration config)
    {
        var preview = TournamentFixtureMatchPlanner.Plan(stored.Definition, stored.ConfirmedResults,
            stored.PlayoffResults, config.DefaultGroupBestOf, config.DefaultPlayoffBestOf);
        var assigned = TournamentMatchAssignmentPlan.Ready(stored, config);
        CollectionAssert.AreEqual(
            preview.Select(match => (match.SourceId, match.Stage == "playoff", match.HomeTeamId,
                match.AwayTeamId, match.Match.BestOf)).ToArray(),
            assigned.Select(match => (match.Id, match.IsPlayoff, match.HomeTeamId,
                match.AwayTeamId, match.BestOf)).ToArray());
    }

    private static TournamentMatchZyConfiguration Configuration() => new()
    {
        Enabled = true,
        ResultBaseUrl = "https://example.org",
        DefaultGroupBestOf = 3,
        DefaultPlayoffBestOf = 3,
    };

    private static TournamentEventDefinition Definition(int count) => new()
    {
        Id = "parity-event",
        Name = "Parity Cup",
        Teams = Enumerable.Range(1, count).Select(i =>
        {
            var roster = Enumerable.Range(0, 5)
                .Select(j => 76561198055500000UL + (ulong)(i * 10 + j)).ToList();
            return new TournamentEventTeamDefinition
            {
                Id = $"team-{i}",
                Name = $"Team {i}",
                Tag = $"T{i}",
                CaptainSteamId = roster[0],
                Starters = roster,
                InitialSeed = i,
            };
        }).ToList(),
    };
}
