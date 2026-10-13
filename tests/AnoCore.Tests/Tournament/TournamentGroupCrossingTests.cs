using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentGroupCrossingTests
{
    [TestMethod]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public void GroupQualifiersAvoidImmediateRematchesWithoutRemovingHighSeedByes(int groups)
    {
        var definition = Definition(groups, 2);
        var results = ConfirmEveryGroupFixture(definition);
        var original = TournamentEventPlayoffs.ResolveEntrants(definition, results)!;
        definition.CrossGroupOpeningPairings = true;
        var crossed = TournamentEventPlayoffs.ResolveEntrants(definition, results)!;
        Assert.AreEqual(original.Count, crossed.Count);
        CollectionAssert.AreEquivalent(original.Select(entry => entry.TeamId).ToArray(),
            crossed.Select(entry => entry.TeamId).ToArray());
        AssertNoSameGroupOpeningPair(definition, crossed);
        var again = TournamentEventPlayoffs.ResolveEntrants(definition, results.AsEnumerable().Reverse().ToArray())!;
        CollectionAssert.AreEqual(crossed.ToArray(), again.ToArray());

        var before = TournamentSingleElimination.GenerateFinalStage(original);
        var after = TournamentSingleElimination.GenerateFinalStage(crossed);
        var byesBefore = original.Select(entry => entry.TeamId)
            .Except(before.Rounds[0].Matches.SelectMany(match =>
                new[] { match.Home.TeamId!, match.Away.TeamId! })).ToArray();
        var byesAfter = crossed.Select(entry => entry.TeamId)
            .Except(after.Rounds[0].Matches.SelectMany(match =>
                new[] { match.Home.TeamId!, match.Away.TeamId! })).ToArray();
        CollectionAssert.AreEquivalent(byesBefore, byesAfter);
        Assert.IsNotNull(TournamentEventPlayoffs.Progress(definition, results, []));
    }

    [TestMethod]
    public void ThreeGroupsWithSixQualifiersActuallyNeedAndReceiveCrossedOpponents()
    {
        var definition = Definition(3, 2);
        var results = ConfirmEveryGroupFixture(definition);
        var baseEntrants = TournamentEventPlayoffs.ResolveEntrants(definition, results)!;
        var groups = GroupMap(definition);
        var baseOpenings = TournamentSingleElimination.GenerateFinalStage(baseEntrants).Rounds[0].Matches;
        Assert.IsTrue(baseOpenings.Any(match => groups[match.Home.TeamId!] == groups[match.Away.TeamId!]));
        definition.CrossGroupOpeningPairings = true;
        var crossed = TournamentEventPlayoffs.ResolveEntrants(definition, results)!;
        AssertNoSameGroupOpeningPair(definition, crossed);
        Assert.IsTrue(baseEntrants.Where((entry, index) => entry.TeamId != crossed[index].TeamId).Any());
    }

    [TestMethod]
    public void AGroupWithThreeTeamsStillCrossesWithOtherGroupsAndQualifiers()
    {
        var definition = Definition(3, 3);
        var results = ConfirmEveryGroupFixture(definition);
        definition.CrossGroupOpeningPairings = true;
        var crossed = TournamentEventPlayoffs.ResolveEntrants(definition, results)!;
        Assert.AreEqual(6, crossed.Count);
        AssertNoSameGroupOpeningPair(definition, crossed);
        var playoffPlan = TournamentEventPlayoffs.Progress(definition, results, [])!;
        Assert.AreEqual(2, playoffPlan.Matches.Count(match => match.IsReady));
    }

    [TestMethod]
    public void InvalidCrossingConfigurationIsRejectedBeforeImport()
    {
        var definition = Definition(3, 2);
        definition.CrossGroupOpeningPairings = true;
        Assert.IsEmpty(TournamentEventDefinition.Validate(definition));
        definition.PlayoffQualifiersPerGroup = null;
        Assert.IsNotEmpty(TournamentEventDefinition.Validate(definition));
        definition.PlayoffQualifiersPerGroup = 2;
        definition.Groups.Clear();
        Assert.IsNotEmpty(TournamentEventDefinition.Validate(definition));
        definition.Groups = Definition(3, 2).Groups;
        definition.GroupStageEnabled = false;
        Assert.IsNotEmpty(TournamentEventDefinition.Validate(definition));
    }

    [TestMethod]
    public void DirectSeededPlayoffsDoNotChangeWhenCrossingIsDisabled()
    {
        var definition = Definition(2, 2);
        definition.GroupStageEnabled = false;
        definition.PlayoffQualifiersPerGroup = null;
        var entrants = TournamentEventPlayoffs.ResolveEntrants(definition, [])!;
        CollectionAssert.AreEqual(new[] { "team-1", "team-2", "team-3", "team-4" },
            entrants.Select(entry => entry.TeamId).ToArray());
    }

    private static void AssertNoSameGroupOpeningPair(TournamentEventDefinition definition,
        IReadOnlyList<TournamentBracketEntrant> entrants)
    {
        var groups = GroupMap(definition);
        var matches = TournamentSingleElimination.GenerateFinalStage(entrants).Rounds[0].Matches;
        foreach (var match in matches)
            Assert.AreNotEqual(groups[match.Home.TeamId!], groups[match.Away.TeamId!],
                $"Opening match {match.Id} would immediately replay a group fixture.");
    }

    private static Dictionary<string, string> GroupMap(TournamentEventDefinition definition)
        => definition.Groups.SelectMany(group => group.TeamIds.Select(id => (id, group.Id)))
            .ToDictionary(pair => pair.id, pair => pair.Id, StringComparer.Ordinal);

    private static TournamentGroupResult[] ConfirmEveryGroupFixture(TournamentEventDefinition definition)
        => definition.Groups.SelectMany(group => TournamentRoundRobin.Generate(definition, group.Id)
            .SelectMany(round => round.Fixtures))
            .Select(fixture => new TournamentGroupResult(fixture.Id, 1, 0, 13, 4)).ToArray();

    private static TournamentEventDefinition Definition(int groupCount, int playersPerGroup)
    {
        var count = groupCount * playersPerGroup;
        var teams = Enumerable.Range(1, count).Select(i =>
        {
            var roster = Enumerable.Range(0, 5)
                .Select(j => 76561198064900000UL + (ulong)(i * 10 + j)).ToList();
            return new TournamentEventTeamDefinition
            {
                Id = $"team-{i}",
                Name = $"Team {i}",
                Tag = $"T{i}",
                CaptainSteamId = roster[0],
                Starters = roster,
                InitialSeed = i,
            };
        }).ToList();
        return new TournamentEventDefinition
        {
            Id = "crossing-cup",
            Name = "Group crossing",
            Teams = teams,
            Groups = Enumerable.Range(0, groupCount).Select(index => new TournamentGroupDefinition
            {
                Id = $"group-{index + 1}",
                Name = $"Group {index + 1}",
                TeamIds = teams.Skip(index * playersPerGroup).Take(playersPerGroup)
                    .Select(team => team.Id).ToList(),
            }).ToList(),
            PlayoffQualifiersPerGroup = 2,
        };
    }
}
