namespace AnoCore.Modules.Tournament;

/// <summary>
/// Keeps higher seeded playoff byes intact while reducing rematches between
/// qualifiers from the same group in the opening knockout round.
/// </summary>
public static class TournamentGroupCrossing
{
    public static IReadOnlyList<TournamentBracketEntrant> Arrange(
        IReadOnlyCollection<TournamentBracketEntrant> entrants,
        IReadOnlyDictionary<string, string> groupByTeam)
    {
        ArgumentNullException.ThrowIfNull(entrants);
        ArgumentNullException.ThrowIfNull(groupByTeam);
        var original = entrants.OrderBy(entry => entry.Seed).ToArray();
        var bracket = TournamentSingleElimination.GenerateFinalStage(original);
        var entryIds = original.Select(entry => entry.TeamId).ToArray();
        if (entryIds.Any(id => !groupByTeam.ContainsKey(id)))
            throw new ArgumentException("Each qualified team needs one source group.", nameof(groupByTeam));
        var sourceSeed = original.ToDictionary(entry => entry.TeamId, entry => entry.Seed,
            StringComparer.Ordinal);
        var pairs = bracket.Rounds[0].Matches.Select(match =>
            (Home: sourceSeed[match.Home.TeamId!], Away: sourceSeed[match.Away.TeamId!])).ToArray();

        // Only first-round matches are permutable; higher seeds with an opening
        // bye retain their seed and guaranteed bye.
        var activeSeeds = pairs.SelectMany(pair => new[] { pair.Home, pair.Away })
            .OrderBy(seed => seed).ToArray();
        if (activeSeeds.Length < 4) return original;

        var candidates = activeSeeds.Select(seed => entryIds[seed - 1]).ToArray();
        var chosen = new string[candidates.Length];
        string[] best = candidates.ToArray();
        var bestClashes = int.MaxValue;
        var bestMovement = int.MaxValue;
        var used = new bool[candidates.Length];

        void Search(int index, int movement)
        {
            if (index == chosen.Length)
            {
                var current = entryIds.ToArray();
                for (var i = 0; i < activeSeeds.Length; i++)
                    current[activeSeeds[i] - 1] = chosen[i];
                var clashes = pairs.Count(pair => string.Equals(
                    groupByTeam[current[pair.Home - 1]],
                    groupByTeam[current[pair.Away - 1]], StringComparison.OrdinalIgnoreCase));
                if (clashes < bestClashes || clashes == bestClashes && movement < bestMovement)
                {
                    bestClashes = clashes;
                    bestMovement = movement;
                    best = chosen.ToArray();
                }
                return;
            }
            // Retain original seed order as deterministic tie-breaker.
            for (var i = 0; i < candidates.Length; i++)
            {
                if (used[i]) continue;
                var displacement = activeSeeds[index] - activeSeeds[i];
                var cost = movement + displacement * displacement;
                if (bestClashes == 0 && cost >= bestMovement) continue;
                used[i] = true;
                chosen[index] = candidates[i];
                Search(index + 1, cost);
                used[i] = false;
            }
        }

        Search(0, 0);
        for (var i = 0; i < activeSeeds.Length; i++)
            entryIds[activeSeeds[i] - 1] = best[i];
        return entryIds.Select((id, index) => new TournamentBracketEntrant(id, index + 1)).ToArray();
    }
}
