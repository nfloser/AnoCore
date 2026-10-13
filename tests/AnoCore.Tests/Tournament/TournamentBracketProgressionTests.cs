using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentBracketProgressionTests
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
    public void ConfirmedWinnersAdvanceUntilExactlyOneChampion(int count)
    {
        var entries = Enumerable.Range(1, count).Select(seed => new TournamentBracketEntrant($"team-{seed}", seed)).ToArray();
        var results = new List<TournamentBracketResult>();
        TournamentBracketProgressSnapshot snapshot;
        do
        {
            snapshot = TournamentBracketProgression.Calculate(entries, results);
            if (snapshot.ChampionTeamId is not null) break;
            Assert.IsTrue(snapshot.Matches.Any(match => match.IsReady));
            foreach (var match in snapshot.Matches.Where(match => match.IsReady))
                results.Add(new(match.Id, match.HomeTeamId!, 2, 0));
        } while (results.Count <= count - 1);
        Assert.AreEqual(count - 1, results.Count);
        Assert.IsNotNull(snapshot.ChampionTeamId);
        Assert.IsFalse(snapshot.Matches.Any(match => match.IsReady));
        Assert.IsTrue(snapshot.Matches.All(match => match.WinnerTeamId is not null));
        var reversed = TournamentBracketProgression.Calculate(entries, results.AsEnumerable().Reverse().ToArray());
        Assert.AreEqual(snapshot.ChampionTeamId, reversed.ChampionTeamId);
    }

    [TestMethod]
    public void InvalidOrPrematureResultsAreRejected()
    {
        TournamentBracketEntrant[] entries = [new("a", 1), new("b", 2), new("c", 3)];
        var initial = TournamentBracketProgression.Calculate(entries, []);
        var ready = initial.Matches.Single(match => match.IsReady);
        var final = initial.Matches.Single(match => !match.IsReady);
        Assert.ThrowsExactly<ArgumentException>(() => TournamentBracketProgression.Calculate(entries, [new("unknown", "a", 1, 0)]));
        Assert.ThrowsExactly<ArgumentException>(() => TournamentBracketProgression.Calculate(entries, [new(final.Id, "a", 1, 0)]));
        Assert.ThrowsExactly<ArgumentException>(() => TournamentBracketProgression.Calculate(entries, [new(ready.Id, "a", 1, 0)]));
        Assert.ThrowsExactly<ArgumentException>(() => TournamentBracketProgression.Calculate(entries, [new(ready.Id, ready.HomeTeamId!, 1, 1)]));
        Assert.ThrowsExactly<ArgumentException>(() => TournamentBracketProgression.Calculate(entries, [new(ready.Id, ready.HomeTeamId!, -1, 0)]));
        var result = new TournamentBracketResult(ready.Id, ready.HomeTeamId!, 1, 0);
        Assert.ThrowsExactly<ArgumentException>(() => TournamentBracketProgression.Calculate(entries, [result, result]));
        Assert.IsNull(initial.ChampionTeamId);
    }
}
