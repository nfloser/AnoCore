using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentGroupQualificationTests
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
    public void GroupsWaitForEveryResultThenQualifyTwoFinalists(int teams)
    {
        var definition = Definition(teams);
        definition.PlayoffQualifiersPerGroup = 2;
        var fixtures = TournamentRoundRobin.Generate(definition, "main").SelectMany(round => round.Fixtures).ToArray();
        var results = fixtures.Select(fixture => new TournamentGroupResult(fixture.Id, 1, 0, 13, 4)).ToArray();
        Assert.IsNull(TournamentEventPlayoffs.ResolveEntrants(definition, results.Skip(1).ToArray()));
        var entrants = TournamentEventPlayoffs.ResolveEntrants(definition, results)!;
        Assert.HasCount(2, entrants);
        var standings = TournamentStandings.Calculate(definition, "main", results);
        Assert.AreEqual(standings[0].TeamId, entrants[0].TeamId);
        Assert.AreEqual(standings[1].TeamId, entrants[1].TeamId);
        var progress = TournamentEventPlayoffs.Progress(definition, results, []);
        Assert.HasCount(1, progress!.Matches);
        Assert.IsTrue(progress.Matches.Single().IsReady);
        var final = progress.Matches.Single();
        var completed = TournamentEventPlayoffs.Progress(definition, results, [new(final.Id, final.HomeTeamId!, 1, 0)])!;
        Assert.AreEqual(final.HomeTeamId, completed.ChampionTeamId);
        Assert.ThrowsExactly<ArgumentException>(() => TournamentEventPlayoffs.Progress(definition, [], [new(final.Id, final.HomeTeamId!, 1, 0)]));
    }

    [TestMethod]
    public void QualificationDoesNotChangeLegacyDirectSeedingAndRejectsInvalidPolicy()
    {
        var definition = Definition(3);
        CollectionAssert.AreEqual(TournamentEventPlayoffs.Entrants(definition).ToArray(), TournamentEventPlayoffs.ResolveEntrants(definition, [])!.ToArray());
        definition.PlayoffQualifiersPerGroup = 1;
        Assert.IsNotEmpty(TournamentEventDefinition.Validate(definition));
        definition.PlayoffQualifiersPerGroup = 4;
        Assert.IsNotEmpty(TournamentEventDefinition.Validate(definition));
    }

    private static TournamentEventDefinition Definition(int count) => new()
    {
        Id = "qualify",
        Name = "Qualification",
        Teams = Enumerable.Range(1, count).Select(i => new TournamentEventTeamDefinition
        {
            Id = $"team-{i}",
            Name = $"Team {i}",
            Tag = $"T{i}",
            InitialSeed = i,
            CaptainSteamId = (ulong)(i * 10),
            Starters = Enumerable.Range(0, 5).Select(j => (ulong)(i * 10 + j)).ToList(),
        }).ToList(),
    };
}
