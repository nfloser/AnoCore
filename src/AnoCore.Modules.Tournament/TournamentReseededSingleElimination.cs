namespace AnoCore.Modules.Tournament;

/// <summary>
/// Single-elimination knockout re-pairing: after a complete round, the strongest
/// surviving seed faces the weakest, preserving confirmed match identities.
/// </summary>
public static class TournamentReseededSingleElimination
{
    public static (TournamentBracketPlan Plan, TournamentBracketProgressSnapshot Progress) Calculate(
        IReadOnlyCollection<TournamentBracketEntrant> entrants,
        IReadOnlyCollection<TournamentBracketResult> confirmedResults)
    {
        ArgumentNullException.ThrowIfNull(confirmedResults);
        var opening = TournamentSingleElimination.GenerateFinalStage(entrants);
        var ordered = entrants.OrderBy(entry => entry.Seed).ToArray();
        var seeds = ordered.ToDictionary(entry => entry.TeamId, entry => entry.Seed, StringComparer.Ordinal);
        var submitted = new Dictionary<string, TournamentBracketResult>(StringComparer.Ordinal);
        foreach (var result in confirmedResults)
            if (result is null || result.MatchId is null || !submitted.TryAdd(result.MatchId, result)
                || string.IsNullOrWhiteSpace(result.WinnerTeamId)
                || result.LoserMaps < 0 || result.WinnerMaps <= result.LoserMaps
                || result.WinnerMaps > 3)
                throw new ArgumentException("Invalid or duplicated reseeded playoff result.", nameof(confirmedResults));

        var roundPlan = new List<TournamentBracketRound>();
        var progress = new List<TournamentBracketMatchProgress>();
        var knownMatches = new HashSet<string>(StringComparer.Ordinal);
        var round = 1;
        var matches = opening.Rounds[0].Matches;
        var playing = matches.SelectMany(match => new[] { match.Home.TeamId!, match.Away.TeamId! })
            .ToHashSet(StringComparer.Ordinal);
        var byes = ordered.Select(entry => entry.TeamId)
            .Where(team => !playing.Contains(team)).ToList();
        string? champion = null;

        while (true)
        {
            roundPlan.Add(new TournamentBracketRound(round, matches));
            var winners = new List<string>();
            var complete = true;
            foreach (var match in matches)
            {
                knownMatches.Add(match.Id);
                var home = match.Home.TeamId!;
                var away = match.Away.TeamId!;
                string? winner = null;
                if (submitted.TryGetValue(match.Id, out var result))
                {
                    if (result.WinnerTeamId != home && result.WinnerTeamId != away)
                        throw new ArgumentException("Confirmed winner is not in the reseeded pairing.", nameof(confirmedResults));
                    winner = result.WinnerTeamId;
                    winners.Add(winner);
                }
                else complete = false;
                progress.Add(new TournamentBracketMatchProgress(match.Id, round, home, away, winner));
            }
            if (!complete) break;
            var survivors = round == 1 ? winners.Concat(byes).ToArray() : winners.ToArray();
            if (survivors.Length == 1)
            {
                champion = survivors[0];
                break;
            }
            if (survivors.Length < 2 || survivors.Length % 2 != 0)
                throw new InvalidOperationException("A completed reseeding round must leave an even number of entrants.");

            // Read only confirmed survivors; no speculative opponents or result
            // dependency references can drift when prior matches are corrected.
            var ranked = survivors.OrderBy(team => seeds[team]).ToArray();
            round++;
            matches = Enumerable.Range(0, ranked.Length / 2).Select(i =>
                new TournamentBracketMatch($"r{round}-m{i + 1}", round,
                    new TournamentBracketSlot(ranked[i], null),
                    new TournamentBracketSlot(ranked[ranked.Length - 1 - i], null))).ToArray();
        }

        if (submitted.Keys.Any(id => !knownMatches.Contains(id)))
            throw new ArgumentException("A result references an ungenerated future playoff round.", nameof(confirmedResults));
        return (new TournamentBracketPlan(opening.SlotCount, opening.OpeningSlots,
                roundPlan.AsReadOnly()),
            new TournamentBracketProgressSnapshot(progress.AsReadOnly(), champion));
    }
}
