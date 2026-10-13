using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentSingleEliminationTests
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
    public void SeededBracket_UsesPowerOfTwoSlotsAndExactlyOneLessMatchThanTeams(int teamCount)
    {
        var entrants = Entries(teamCount).Reverse().ToArray();

        var bracket = TournamentSingleElimination.Generate(entrants);
        var matches = bracket.Rounds.SelectMany(round => round.Matches).ToArray();

        Assert.AreEqual(teamCount <= 4 ? 4 : teamCount <= 8 ? 8 : 16, bracket.SlotCount);
        Assert.AreEqual(teamCount - 1, matches.Length);
        Assert.AreEqual((int)Math.Log2(bracket.SlotCount), bracket.Rounds.Count);
        Assert.AreEqual(bracket.SlotCount, bracket.OpeningSlots.Count);
        Assert.AreEqual(teamCount, bracket.OpeningSlots.Count(slot => slot.TeamId is not null));
        Assert.AreEqual(bracket.SlotCount - teamCount, bracket.OpeningSlots.Count(slot => slot.TeamId is null));
        Assert.IsTrue(matches.All(match => match.Home.HasValue && match.Away.HasValue));

        var earlierIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var round in bracket.Rounds)
        {
            foreach (var match in round.Matches)
            {
                Assert.AreEqual(round.Number, match.Round);
                Assert.IsFalse(earlierIds.Contains(match.Id));
                foreach (var slot in new[] { match.Home, match.Away })
                {
                    if (slot.FromMatchId is not null)
                        Assert.IsTrue(earlierIds.Contains(slot.FromMatchId),
                            "A match may only depend on a completed earlier bracket node.");
                }
            }
            foreach (var match in round.Matches)
                earlierIds.Add(match.Id);
        }

        var ordered = TournamentSingleElimination.Generate(Entries(teamCount));
        CollectionAssert.AreEqual(
            matches.Select(match => match.ToString()).ToArray(),
            ordered.Rounds.SelectMany(round => round.Matches)
                .Select(match => match.ToString()).ToArray());
    }

    [TestMethod]
    public void ThreeTeams_GivesTopSeedByeAndSecondPlaysThird()
    {
        var bracket = TournamentSingleElimination.Generate(Entries(3));

        Assert.AreEqual(1, bracket.Rounds[0].Matches.Count);
        var semifinal = bracket.Rounds[0].Matches.Single();
        CollectionAssert.AreEquivalent(new[] { "team-2", "team-3" },
            new[] { semifinal.Home.TeamId!, semifinal.Away.TeamId! });
        var final = bracket.Rounds[1].Matches.Single();
        Assert.AreEqual("team-1", final.Home.TeamId);
        Assert.AreEqual(semifinal.Id, final.Away.FromMatchId);
    }

    [TestMethod]
    public void FiveTeams_ProtectsTopThreeSeedsWithOpeningByes()
    {
        var bracket = TournamentSingleElimination.Generate(Entries(5));

        Assert.AreEqual(1, bracket.Rounds[0].Matches.Count);
        var opening = bracket.Rounds[0].Matches.Single();
        CollectionAssert.AreEquivalent(new[] { "team-4", "team-5" },
            new[] { opening.Home.TeamId!, opening.Away.TeamId! });
        var firstHalf = bracket.OpeningSlots.Take(bracket.SlotCount / 2);
        var secondHalf = bracket.OpeningSlots.Skip(bracket.SlotCount / 2);
        Assert.IsTrue(firstHalf.Any(slot => slot.TeamId == "team-1"));
        Assert.IsTrue(secondHalf.Any(slot => slot.TeamId == "team-2"));
    }

    [TestMethod]
    public void InvalidSeedsAndTeamIdsAreRejectedWithoutMutatingInput()
    {
        var entries = Entries(4);
        var original = entries.Select(entry => entry.ToString()).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() =>
            TournamentSingleElimination.Generate([entries[0], entries[0], entries[2]]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            TournamentSingleElimination.Generate([entries[0], entries[1] with { Seed = 1 }, entries[2]]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            TournamentSingleElimination.Generate([entries[0], entries[1] with { TeamId = "team-1" }, entries[2]]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            TournamentSingleElimination.Generate(Entries(2)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            TournamentSingleElimination.Generate(Entries(11)));
        CollectionAssert.AreEqual(original, entries.Select(entry => entry.ToString()).ToArray());
    }

    private static TournamentBracketEntrant[] Entries(int count)
        => Enumerable.Range(1, count)
            .Select(seed => new TournamentBracketEntrant($"team-{seed}", seed)).ToArray();
}
