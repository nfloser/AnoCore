using System.Text.Json;
using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentMatchZyContractTests
{
    [TestMethod]
    public void MatchConfigUsesNumericMatchIdAndFiveReadyPlayersPerTeam()
    {
        var definition = Definition();
        var binding = new TournamentMatchAssignment(42, "event", "fixture", false, "team-1", "team-2", "server", Guid.NewGuid(), 3, ["de_mirage", "de_nuke", "de_ancient"], "pending");
        using var document = JsonDocument.Parse(TournamentMatchZy.CreateConfiguration(definition, binding, "https://example.com/tournament/matchzy/42", new string('a', 64)));
        var json = document.RootElement;
        Assert.AreEqual(42L, json.GetProperty("matchid").GetInt64());
        Assert.AreEqual(5, json.GetProperty("min_players_to_ready").GetInt32());
        Assert.AreEqual(5, json.GetProperty("team1").GetProperty("players").EnumerateObject().Count());
        Assert.AreEqual("team-1", json.GetProperty("team1").GetProperty("id").GetString());
        Assert.IsTrue(json.GetProperty("skip_veto").GetBoolean());
    }

    [TestMethod]
    public void CompletedSeriesWaitsForMapEventsAndReplaysAreIdempotent()
    {
        var state = new TournamentMatchZyResultState();
        var series = """{"event":"series_end","matchid":42,"winner":{"team":"team1","side":"CT"},"team1_series_score":2,"team2_series_score":1}""";
        state = TournamentMatchZy.Apply(state, series, 42, 3);
        Assert.IsNull(TournamentMatchZy.Completed(state, 3));
        for (var map = 0; map < 3; map++)
        {
            var winner = map == 1 ? "team2" : "team1";
            var payload = $$$"""{"event":"map_result","matchid":42,"map_number":{{{map}}},"winner":{"team":"{{{winner}}}","side":"T"},"team1":{"id":"team-1","score":{{{(map == 1 ? 4 : 13)}}}},"team2":{"id":"team-2","score":{{{(map == 1 ? 13 : 4)}}}}}""";
            state = TournamentMatchZy.Apply(state, payload, 42, 3);
            var replay = TournamentMatchZy.Apply(state, payload, 42, 3);
            Assert.AreEqual(JsonSerializer.Serialize(state), JsonSerializer.Serialize(replay));
        }
        var result = TournamentMatchZy.Completed(state, 3)!;
        Assert.AreEqual(2, result.HomeMaps);
        Assert.AreEqual(1, result.AwayMaps);
        Assert.AreEqual(30, result.HomeRounds);
        Assert.ThrowsExactly<ArgumentException>(() => TournamentMatchZy.Apply(state, series.Replace("42", "43"), 42, 3));
        Assert.ThrowsExactly<ArgumentException>(() => TournamentMatchZy.Apply(state, series.Replace("\"team1\"", "\"team2\""), 42, 3));
    }

    private static TournamentEventDefinition Definition() => new()
    {
        Id = "event",
        Name = "Event",
        Teams = Enumerable.Range(1, 3).Select(i => new TournamentEventTeamDefinition
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
