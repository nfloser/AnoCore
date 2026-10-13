using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentRoundReseedingTests
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
    public void EverySupportedSizeReseedsToOneChampionWithoutDuplicateOpponents(int count)
    {
        var definition = Definition(count);
        for (var randomSeed = 0; randomSeed < 6; randomSeed++)
        {
            var random = new Random(randomSeed);
            var results = new List<TournamentBracketResult>();
            TournamentBracketProgressSnapshot progress;
            do
            {
                progress = TournamentEventPlayoffs.Progress(definition, [], results)!;
                if (progress.ChampionTeamId is not null) break;
                var candidates = progress.Matches.Where(match => match.IsReady).ToArray();
                Assert.IsNotEmpty(candidates);
                foreach (var match in candidates)
                {
                    Assert.AreNotEqual(match.HomeTeamId, match.AwayTeamId);
                    var winner = random.Next(2) == 0 ? match.HomeTeamId! : match.AwayTeamId!;
                    results.Add(new(match.Id, winner, 1, 0));
                }
            } while (true);
            Assert.AreEqual(count - 1, results.Count);
            Assert.AreEqual(count - 1, progress.Matches.Count);
            Assert.IsNotNull(progress.ChampionTeamId);
            Assert.IsTrue(definition.Teams.Any(team => team.Id == progress.ChampionTeamId));
            Assert.AreEqual(count - 1, results.Select(result => result.MatchId).Distinct().Count());
            Assert.IsFalse(progress.Matches.Any(match => match.IsReady));
            var snapshot = TournamentReseededSingleElimination.Calculate(
                TournamentEventPlayoffs.Entrants(definition), results);
            Assert.AreEqual(progress.ChampionTeamId, snapshot.Progress.ChampionTeamId);
            CollectionAssert.AreEqual(progress.Matches.Select(match => match.Id).ToArray(),
                snapshot.Plan.Rounds.SelectMany(round => round.Matches).Select(match => match.Id).ToArray());
        }
    }

    [TestMethod]
    public void UpsetsAreRepairedByActualSeedRatherThanStaticBracketDependencies()
    {
        var definition = Definition(8);
        var opening = TournamentEventPlayoffs.Progress(definition, [], [])!.Matches.ToArray();
        var winners = new[] { "team-8", "team-4", "team-2", "team-6" };
        var results = opening.Select((match, i) => new TournamentBracketResult(match.Id, winners[i], 1, 0)).ToArray();
        var progress = TournamentEventPlayoffs.Progress(definition, [], results)!;
        var second = progress.Matches.Where(match => match.Round == 2).ToArray();
        Assert.HasCount(2, second);
        Assert.AreEqual("team-2", second[0].HomeTeamId);
        Assert.AreEqual("team-8", second[0].AwayTeamId);
        Assert.AreEqual("team-4", second[1].HomeTeamId);
        Assert.AreEqual("team-6", second[1].AwayTeamId);
        Assert.IsTrue(second.All(match => match.IsReady));
        Assert.IsNull(progress.ChampionTeamId);
    }

    [TestMethod]
    public void ByesRemainWithOriginalHighSeedsDuringOpeningRound()
    {
        var definition = Definition(10);
        var entrants = TournamentEventPlayoffs.Entrants(definition);
        var first = TournamentReseededSingleElimination.Calculate(entrants, []);
        Assert.AreEqual(2, first.Progress.Matches.Count);
        var opening = first.Progress.Matches;
        var firstWinners = opening.Select(match =>
            new TournamentBracketResult(match.Id, match.HomeTeamId!, 1, 0)).ToArray();
        var next = TournamentReseededSingleElimination.Calculate(entrants, firstWinners);
        var participants = next.Progress.Matches.Where(match => match.Round == 2)
            .SelectMany(match => new[] { match.HomeTeamId!, match.AwayTeamId! }).ToArray();
        CollectionAssert.AreEquivalent(Enumerable.Range(1, 6).Select(i => $"team-{i}")
                .Concat(opening.Select(match => match.HomeTeamId!)).ToArray(),
            participants);
        Assert.AreEqual(4, next.Progress.Matches.Count(match => match.Round == 2));
    }

    [TestMethod]
    public void ReseedingRejectsFutureResultsBeforePriorRoundCompletes()
    {
        var definition = Definition(8);
        var entrants = TournamentEventPlayoffs.Entrants(definition);
        Assert.ThrowsExactly<ArgumentException>(() =>
            TournamentReseededSingleElimination.Calculate(entrants,
                [new("r2-m1", "team-1", 1, 0)]));
        var first = TournamentReseededSingleElimination.Calculate(entrants, []).Progress.Matches[0];
        Assert.ThrowsExactly<ArgumentException>(() =>
            TournamentReseededSingleElimination.Calculate(entrants,
                [new(first.Id, "team-999", 1, 0)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            TournamentReseededSingleElimination.Calculate(entrants,
                [new(first.Id, first.HomeTeamId!, 1, 0), new(first.Id, first.HomeTeamId!, 1, 0)]));
    }

    [TestMethod]
    public void ReseedingIsAnExplicitValidSingleEliminationOnlyPolicy()
    {
        var definition = Definition(5);
        Assert.IsEmpty(TournamentEventDefinition.Validate(definition));
        definition.PlayoffFormat = TournamentPlayoffFormat.DoubleElimination;
        Assert.IsNotEmpty(TournamentEventDefinition.Validate(definition));
        definition.PlayoffFormat = TournamentPlayoffFormat.SingleElimination;
        definition.PlayoffsEnabled = false;
        Assert.IsNotEmpty(TournamentEventDefinition.Validate(definition));
        definition.PlayoffsEnabled = true;
        var staticDefinition = Definition(5);
        staticDefinition.ReseedPlayoffRounds = false;
        Assert.AreEqual(1, TournamentPlayoffPlans.Generate(staticDefinition,
            TournamentEventPlayoffs.Entrants(staticDefinition)).Rounds[0].Matches.Count);
    }

    private static TournamentEventDefinition Definition(int count) => new()
    {
        Id = "reseeding",
        Name = "Reseeding",
        GroupStageEnabled = false,
        PlayoffsEnabled = true,
        ReseedPlayoffRounds = true,
        PlayoffBestOf = 1,
        Teams = Enumerable.Range(1, count).Select(i =>
        {
            var roster = Enumerable.Range(0, 5)
                .Select(j => 76561198088000000UL + (ulong)(i * 10 + j)).ToList();
            return new TournamentEventTeamDefinition
            {
                Id = $"team-{i}",
                Name = $"Team {i}",
                Tag = $"T{i}",
                InitialSeed = i,
                CaptainSteamId = roster[0],
                Starters = roster,
            };
        }).ToList(),
    };
}
