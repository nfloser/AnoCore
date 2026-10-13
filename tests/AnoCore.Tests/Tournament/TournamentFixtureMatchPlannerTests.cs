using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentFixtureMatchPlannerTests
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
    public void RoundRobinPlansAllOutstandingFixturesWithStableFivePlayerRosters(int count)
    {
        var definition = Definition(count);
        definition.PlayoffsEnabled = false;
        definition.GroupBestOf = 3;

        var first = TournamentFixtureMatchPlanner.Plan(definition, [], []);
        var second = TournamentFixtureMatchPlanner.Plan(definition, [], []);
        Assert.AreEqual(count * (count - 1) / 2, first.Count);
        CollectionAssert.AreEqual(first.Select(item => item.Match.MatchId).ToArray(),
            second.Select(item => item.Match.MatchId).ToArray());
        Assert.AreEqual(first.Count, first.Select(item => item.Match.MatchId).Distinct().Count());
        foreach (var item in first)
        {
            Assert.AreEqual("group", item.Stage);
            Assert.AreEqual(3, item.Match.BestOf);
            Assert.AreEqual(5, item.Match.TeamA.Members.Count);
            Assert.AreEqual(5, item.Match.TeamB.Members.Count);
            Assert.IsTrue(item.Match.Enabled);
            Assert.IsEmpty(TournamentMatchDefinition.Validate(item.Match));
            Assert.AreNotEqual(Guid.Empty, Guid.Parse(item.Match.MatchId));
        }

        var firstFixture = first[0];
        var recorded = new TournamentGroupResult(firstFixture.SourceId, 2, 0, 26, 13);
        var remaining = TournamentFixtureMatchPlanner.Plan(definition, [recorded], []);
        Assert.AreEqual(first.Count - 1, remaining.Count);
        Assert.IsFalse(remaining.Any(item => item.Match.MatchId == firstFixture.Match.MatchId));
    }

    [TestMethod]
    public void SwissUnlocksNextRoundOnlyAfterCurrentRoundIsComplete()
    {
        var definition = Definition(5);
        definition.PlayoffsEnabled = false;
        definition.GroupFormat = TournamentGroupFormat.Swiss;
        definition.SwissRounds = 2;
        var initial = TournamentFixtureMatchPlanner.Plan(definition, [], []);
        Assert.AreEqual(2, initial.Count);

        var first = new TournamentGroupResult(initial[0].SourceId, 1, 0, 13, 6);
        Assert.AreEqual(1, TournamentFixtureMatchPlanner.Plan(definition, [first], []).Count);
        var second = new TournamentGroupResult(initial[1].SourceId, 1, 0, 13, 8);
        var next = TournamentFixtureMatchPlanner.Plan(definition, [first, second], []);
        Assert.AreEqual(2, next.Count);
        Assert.IsTrue(next.All(item => !initial.Any(old => old.SourceId == item.SourceId)));
        Assert.AreEqual(4, next.Select(item => item.Match.TeamA.Members[0])
            .Concat(next.Select(item => item.Match.TeamB.Members[0])).Distinct().Count());
    }

    [TestMethod]
    public void PlayoffsAreGatedAfterGroupsAndResolvePriorWinners()
    {
        var definition = Definition(3);
        definition.GroupBestOf = 1;
        definition.PlayoffBestOf = 3;
        definition.PlayoffQualifiersPerGroup = 2;

        Assert.AreEqual(3, TournamentFixtureMatchPlanner.Plan(definition, [], []).Count);
        var groups = TournamentRoundRobin.Generate(definition, "main").SelectMany(round => round.Fixtures)
            .Select(f => new TournamentGroupResult(f.Id, 1, 0, 13, 6)).ToArray();
        var final = TournamentFixtureMatchPlanner.Plan(definition, groups, []);
        Assert.AreEqual(1, final.Count);
        Assert.AreEqual("playoff", final[0].Stage);
        Assert.AreEqual(3, final[0].Match.BestOf);
        Assert.AreEqual("r1-m1", final[0].SourceId);
        Assert.IsEmpty(TournamentFixtureMatchPlanner.Plan(definition, groups,
            [new TournamentBracketResult(final[0].SourceId, final[0].HomeTeamId, 2, 0)]));
    }

    [TestMethod]
    public void DirectDoubleEliminationUsesReadyPairsAndSameMatchIdsAfterProgress()
    {
        var definition = Definition(4);
        definition.GroupStageEnabled = false;
        definition.PlayoffFormat = TournamentPlayoffFormat.DoubleElimination;
        definition.PlayoffBestOf = 1;

        var opening = TournamentFixtureMatchPlanner.Plan(definition, [], []);
        Assert.AreEqual(2, opening.Count);
        Assert.IsTrue(opening.All(item => item.Stage == "playoff" && item.SourceId.StartsWith("u-", StringComparison.Ordinal)));
        var first = opening[0];
        var result = new TournamentBracketResult(first.SourceId, first.HomeTeamId, 1, 0);
        var after = TournamentFixtureMatchPlanner.Plan(definition, [], [result]);
        Assert.IsFalse(after.Any(item => item.SourceId == first.SourceId));
        var stillOpen = after.Single(item => item.SourceId == opening[1].SourceId);
        Assert.AreEqual(opening[1].Match.MatchId, stillOpen.Match.MatchId);
    }

    [TestMethod]
    public void DeterministicMatchIdentityScopesEventAndStageAndSurvivesTeamRename()
    {
        var first = Definition(3);
        first.PlayoffsEnabled = false;
        var original = TournamentFixtureMatchPlanner.Plan(first, [], []);
        first.Teams[0].Name = "Renamed";
        var renamed = TournamentFixtureMatchPlanner.Plan(first, [], []);
        CollectionAssert.AreEqual(original.Select(item => item.Match.MatchId).ToArray(),
            renamed.Select(item => item.Match.MatchId).ToArray());
        Assert.AreEqual("Renamed", renamed.First(item => item.HomeTeamId == "team-1"
            || item.AwayTeamId == "team-1").Match.TeamA.Name == "Renamed" ? "Renamed" :
            renamed.First(item => item.HomeTeamId == "team-1"
            || item.AwayTeamId == "team-1").Match.TeamB.Name);

        var otherEvent = Definition(3);
        otherEvent.Id = "other-event";
        otherEvent.PlayoffsEnabled = false;
        var second = TournamentFixtureMatchPlanner.Plan(otherEvent, [], []);
        Assert.IsTrue(original.Zip(second).All(pair => pair.First.Match.MatchId != pair.Second.Match.MatchId));
    }

    [TestMethod]
    public void SeededPlayoffsWithoutQualificationStillWaitForEnabledGroupStage()
    {
        var definition = Definition(4);
        Assert.IsNull(definition.PlayoffQualifiersPerGroup);
        Assert.IsTrue(definition.PlayoffsEnabled);
        var initial = TournamentFixtureMatchPlanner.Plan(definition, [], []);
        Assert.AreEqual(6, initial.Count);
        Assert.IsTrue(initial.All(item => item.Stage == "group"));

        var results = TournamentRoundRobin.Generate(definition, "main")
            .SelectMany(round => round.Fixtures)
            .Select(fixture => new TournamentGroupResult(fixture.Id, 1, 0, 13, 6)).ToArray();
        var after = TournamentFixtureMatchPlanner.Plan(definition, results, []);
        Assert.AreEqual(2, after.Count);
        Assert.IsTrue(after.All(item => item.Stage == "playoff"));
    }

    [TestMethod]
    public void ConflictingInputAndDisabledStagesAreRejectedOrNotScheduled()
    {
        var definition = Definition(3);
        definition.GroupStageEnabled = false;
        definition.PlayoffsEnabled = false;
        Assert.ThrowsExactly<ArgumentException>(() => TournamentFixtureMatchPlanner.Plan(definition, [], []));
        definition.PlayoffsEnabled = true;
        Assert.AreEqual(1, TournamentFixtureMatchPlanner.Plan(definition, [], []).Count);
        Assert.ThrowsExactly<ArgumentException>(() => TournamentFixtureMatchPlanner.Plan(definition,
            [new TournamentGroupResult("fake", 1, 0, 13, 6)], []));
        Assert.ThrowsExactly<ArgumentException>(() => TournamentFixtureMatchPlanner.Plan(definition, [],
            [new TournamentBracketResult("fake", "team-1", 1, 0)]));
    }

    private static TournamentEventDefinition Definition(int count)
    {
        var teams = Enumerable.Range(1, count).Select(i =>
        {
            var starters = Enumerable.Range(0, 5).Select(j => 76561198021100000UL +
                (ulong)(i * 10 + j)).ToList();
            return new TournamentEventTeamDefinition
            {
                Id = $"team-{i}",
                Name = $"Team {i}",
                Tag = $"T{i}",
                InitialSeed = i,
                Starters = starters,
                CaptainSteamId = starters[0],
            };
        }).ToList();
        return new TournamentEventDefinition
        {
            Id = "cup",
            Name = "Cup",
            Teams = teams,
        };
    }
}
