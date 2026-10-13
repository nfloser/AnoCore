using System.Text.Json.Serialization;

namespace AnoCore.Modules.Tournament;

[JsonConverter(typeof(JsonStringEnumConverter<TournamentGroupFormat>))]
public enum TournamentGroupFormat { RoundRobin = 0, Swiss = 1 }
[JsonConverter(typeof(JsonStringEnumConverter<TournamentPlayoffFormat>))]
public enum TournamentPlayoffFormat { SingleElimination = 0, DoubleElimination = 1 }

/// <summary>One authoritative deterministic schedule for readers, validation and match assignment.</summary>
public static class TournamentSchedules
{
    public static int SwissRoundCount(TournamentEventDefinition definition, int teams)
        => definition.SwissRounds ?? (int)Math.Ceiling(Math.Log2(teams));

    public static IReadOnlyList<TournamentGroupRound> Generate(TournamentEventDefinition definition, string groupId,
        IReadOnlyCollection<TournamentGroupResult> results)
    {
        TournamentEventDefinition.RequireValid(definition);
        ArgumentNullException.ThrowIfNull(results);
        if (!definition.GroupStageEnabled)
        {
            if (results.Count > 0) throw new ArgumentException("The group stage is disabled.", nameof(results));
            return [];
        }
        if (definition.GroupFormat == TournamentGroupFormat.RoundRobin)
            return TournamentRoundRobin.Generate(definition, groupId);
        var group = TournamentEventDefinition.ResolveGroup(definition, groupId);
        if (results.Any(r => r is null || string.IsNullOrEmpty(r.FixtureId)) || results.Select(r => r.FixtureId).Distinct().Count() != results.Count)
            throw new ArgumentException("Swiss results must have unique fixture IDs.", nameof(results));
        var submitted = results.ToDictionary(r => r.FixtureId, StringComparer.Ordinal);
        var initial = definition.Teams.Where(team => group.TeamIds.Contains(team.Id, StringComparer.Ordinal))
            .OrderBy(team => team.InitialSeed ?? int.MaxValue).ThenBy(team => team.Id, StringComparer.Ordinal)
            .Select(team => team.Id).ToArray();
        var rounds = new List<TournamentGroupRound>();
        var played = new HashSet<string>(StringComparer.Ordinal);
        var byes = initial.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var known = new HashSet<string>(StringComparer.Ordinal);
        for (var number = 1; number <= SwissRoundCount(definition, initial.Length); number++)
        {
            var previous = submitted.Values.Where(result => known.Contains(result.FixtureId)).ToArray();
            var standings = TournamentStandings.CalculateScheduled(definition, group.Id, previous, rounds);
            var ranked = number == 1 ? initial : standings.Select(row => row.TeamId).ToArray();
            var points = standings.ToDictionary(row => row.TeamId, row => row.Points, StringComparer.Ordinal);
            List<(string Home, string Away)>? pairing = null;
            string? bye = null;
            var candidates = ranked.Length % 2 == 0 ? new string?[] { null }
                : ranked.Where(id => byes[id] == byes.Values.Min()).OrderBy(id => points[id])
                    .ThenByDescending(id => Array.IndexOf(ranked, id)).Cast<string?>().ToArray();
            foreach (var candidate in candidates)
            {
                var available = ranked.Where(id => id != candidate).ToArray();
                pairing = number == 1 ? available.Take(available.Length / 2)
                    .Zip(available.Skip(available.Length / 2), (home, away) => (Home: home, Away: away)).ToList()
                    : Match(available, ranked, points, played);
                if (pairing is not null) { bye = candidate; break; }
            }
            if (pairing is null) throw new ArgumentException("No fair rematch-free Swiss pairing remains.", nameof(results));
            var fixtures = pairing.Select((pair, index) => new TournamentGroupFixture(
                $"{group.Id}:s{number}:m{index + 1}:{pair.Home}:{pair.Away}", group.Id, number, pair.Home, pair.Away)).ToArray();
            rounds.Add(new(number, fixtures, bye));
            if (bye is not null) byes[bye]++;
            foreach (var fixture in fixtures)
            {
                known.Add(fixture.Id);
                played.Add(PairKey(fixture.HomeTeamId, fixture.AwayTeamId));
            }
            if (fixtures.Any(fixture => !submitted.ContainsKey(fixture.Id))) break;
        }
        _ = TournamentStandings.CalculateScheduled(definition, group.Id, results, rounds);
        return rounds.AsReadOnly();
    }

    public static bool IsComplete(TournamentEventDefinition definition, string groupId, IReadOnlyCollection<TournamentGroupResult> results)
    {
        var rounds = Generate(definition, groupId, results);
        var expected = definition.GroupFormat == TournamentGroupFormat.Swiss
            ? SwissRoundCount(definition, TournamentEventDefinition.ResolveGroup(definition, groupId).TeamIds.Count) : rounds.Count;
        return rounds.Count == expected && rounds.Sum(round => round.Fixtures.Count) == results.Count;
    }

    private static string PairKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "/" + b : b + "/" + a;

    private static List<(string Home, string Away)>? Match(string[] teams, string[] ranked,
        IReadOnlyDictionary<string, int> points, IReadOnlySet<string> played)
    {
        List<(string Home, string Away)>? best = null;
        var bestCost = long.MaxValue;
        void Search(List<string> remaining, List<(string Home, string Away)> pairs, long cost)
        {
            if (cost >= bestCost) return;
            if (remaining.Count == 0) { best = pairs.ToList(); bestCost = cost; return; }
            var home = remaining[0];
            for (var i = 1; i < remaining.Count; i++)
            {
                var away = remaining[i];
                if (played.Contains(PairKey(home, away))) continue;
                var gap = Math.Abs(points[home] - points[away]) * 10000L
                    + Math.Abs(Array.IndexOf(ranked, home) - Array.IndexOf(ranked, away));
                var next = remaining.Where((_, index) => index != 0 && index != i).ToList();
                pairs.Add((home, away));
                Search(next, pairs, cost + gap);
                pairs.RemoveAt(pairs.Count - 1);
            }
        }
        Search(teams.ToList(), [], 0);
        return best;
    }
}
