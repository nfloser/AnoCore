using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentExtendedFormatTests
{
    [TestMethod]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(10)]
    public void DoubleEliminationRequiresTwoLossesAndResolvesAChampion(int count)
    {
        var entrants = Enumerable.Range(1, count).Select(i => new TournamentBracketEntrant($"team-{i}", i)).ToArray();
        for (var seed = 0; seed < 12; seed++)
        {
            var random = new Random(seed);
            var results = new List<TournamentBracketResult>();
            var losses = entrants.ToDictionary(e => e.TeamId, _ => 0);
            TournamentBracketProgressSnapshot progress;
            do
            {
                progress = TournamentDoubleElimination.Calculate(entrants, results, true);
                if (progress.ChampionTeamId is not null) break;
                var match = progress.Matches.First(m => m.IsReady);
                var winner = random.Next(2) == 0 ? match.HomeTeamId! : match.AwayTeamId!;
                var loser = winner == match.HomeTeamId ? match.AwayTeamId! : match.HomeTeamId!;
                losses[loser]++;
                results.Add(new(match.Id, winner, 1, 0));
                Assert.IsTrue(results.Count <= 2 * count - 1);
            } while (true);
            Assert.IsNotNull(progress.ChampionTeamId);
            Assert.IsTrue(losses.Where(pair => pair.Key != progress.ChampionTeamId).All(pair => pair.Value == 2));
            Assert.IsTrue(losses[progress.ChampionTeamId] <= 1);
            Assert.IsTrue(results.Count is var length && (length == 2 * count - 2 || length == 2 * count - 1));
        }
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(10)]
    public void SwissGeneratesCompleteRematchFreeRoundsAndFairByes(int count)
    {
        var definition = Definition(count);
        definition.GroupFormat = TournamentGroupFormat.Swiss;
        var results = new List<TournamentGroupResult>();
        var pairs = new HashSet<string>();
        var byes = new HashSet<string>();
        var lastRound = 0;
        while (!TournamentSchedules.IsComplete(definition, "main", results))
        {
            var round = TournamentSchedules.Generate(definition, "main", results).Last();
            Assert.AreEqual(lastRound + 1, round.Number);
            foreach (var match in round.Fixtures)
            {
                Assert.IsTrue(pairs.Add(string.Join("/", new[] { match.HomeTeamId, match.AwayTeamId }.Order())));
                results.Add(new(match.Id, 1, 0, 13, 4));
            }
            if (round.ByeTeamId is { } bye) Assert.IsTrue(byes.Add(bye));
            Assert.AreEqual(count, round.Fixtures.Count * 2 + (round.ByeTeamId is null ? 0 : 1));
            lastRound++;
        }
        var standings = TournamentStandings.Calculate(definition, "main", results);
        Assert.AreEqual(results.Count * 2, standings.Sum(row => row.GamesPlayed));
        Assert.AreEqual(results.Count * definition.Standings.WinPoints + byes.Count * definition.Standings.WinPoints,
            standings.Sum(row => row.Points));
        TournamentEventSnapshotValidator.Validate(definition, results);
        CollectionAssert.AreEqual(TournamentSchedules.Generate(definition, "main", results).SelectMany(r => r.Fixtures.Select(f => f.Id).Append(r.ByeTeamId ?? "")).ToArray(),
            TournamentSchedules.Generate(definition, "main", results.AsEnumerable().Reverse().ToArray()).SelectMany(r => r.Fixtures.Select(f => f.Id).Append(r.ByeTeamId ?? "")).ToArray());
    }

    [TestMethod]
    public void ContradictoryResultsAndFormatsAreRejected()
    {
        var entries = Enumerable.Range(1, 3).Select(i => new TournamentBracketEntrant($"team-{i}", i)).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => TournamentDoubleElimination.Calculate(entries, [new("grand-final-reset", "team-1", 1, 0)]));
        var definition = Definition(3);
        definition.GroupFormat = TournamentGroupFormat.Swiss;
        definition.SwissRounds = 20;
        Assert.IsNotEmpty(TournamentEventDefinition.Validate(definition));
    }

    [TestMethod]
    public void DisabledStagesRejectResultsAndResolveOnlyEnabledStages()
    {
        var definition = Definition(4);
        definition.GroupStageEnabled = false;
        Assert.IsEmpty(TournamentSchedules.Generate(definition, "main", []));
        Assert.IsNotNull(TournamentEventPlayoffs.Progress(definition, [], []));
        Assert.ThrowsExactly<ArgumentException>(() => TournamentEventSnapshotValidator.Validate(definition,
            [new("main:r1:m1:team-1:team-4", 1, 0, 13, 4)]));
        definition.GroupStageEnabled = true;
        definition.PlayoffsEnabled = false;
        Assert.IsNull(TournamentEventPlayoffs.Progress(definition, [], []));
        Assert.ThrowsExactly<ArgumentException>(() => TournamentEventPlayoffs.Progress(definition, [],
            [new("r1-m1", "team-1", 1, 0)]));
        definition.GroupStageEnabled = false;
        Assert.IsNotEmpty(TournamentEventDefinition.Validate(definition));
    }

    [TestMethod]
    public void SwissAllSupportedSizesAndRoundLimitsRemainPairableWithMixedResults()
    {
        for (var count = 3; count <= 10; count++)
            for (var seed = 0; seed < 100; seed++)
            {
                var definition = Definition(count);
                definition.GroupFormat = TournamentGroupFormat.Swiss;
                definition.SwissRounds = (count + 1) / 2;
                var random = new Random(seed);
                var results = new List<TournamentGroupResult>();
                while (!TournamentSchedules.IsComplete(definition, "main", results))
                    foreach (var fixture in TournamentSchedules.Generate(definition, "main", results).Last().Fixtures)
                    {
                        var homeWins = random.Next(2) == 0;
                        results.Add(new(fixture.Id, homeWins ? 1 : 0, homeWins ? 0 : 1, 13, 4));
                    }
            }
    }

    private static TournamentEventDefinition Definition(int count) => new()
    {
        Id = "formats",
        Name = "Formats",
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
