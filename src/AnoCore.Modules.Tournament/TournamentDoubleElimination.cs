namespace AnoCore.Modules.Tournament;

/// <summary>A deterministic upper/lower dependency graph; byes never create fictitious results.</summary>
public static class TournamentDoubleElimination
{
    public static TournamentBracketPlan Generate(IReadOnlyCollection<TournamentBracketEntrant> entrants, bool resetFinal = true)
    {
        var upper = TournamentSingleElimination.GenerateFinalStage(entrants);
        var rounds = new List<TournamentBracketRound>();
        var lower = new List<TournamentBracketSlot>();
        var lowerNumber = 0;
        TournamentBracketSlot UpperSlot(TournamentBracketSlot slot) => slot.FromMatchId is { } id
            ? new(null, "u-" + id) : slot;
        var upperRounds = upper.Rounds.Select(round => new TournamentBracketRound(round.Number,
            round.Matches.Select(match => new TournamentBracketMatch("u-" + match.Id, match.Round,
                UpperSlot(match.Home), UpperSlot(match.Away))
            { Bracket = "upper" }).ToArray())).ToArray();
        rounds.AddRange(upperRounds);
        List<TournamentBracketSlot> Pair(IReadOnlyList<TournamentBracketSlot> slots)
        {
            if (slots.Count < 2) return slots.ToList();
            var matches = new List<TournamentBracketMatch>();
            var advanced = new List<TournamentBracketSlot>();
            lowerNumber++;
            for (var i = 0; i < slots.Count; i += 2)
            {
                if (i + 1 == slots.Count) { advanced.Add(slots[i]); continue; }
                var id = $"l-r{lowerNumber}-m{matches.Count + 1}";
                matches.Add(new(id, lowerNumber, slots[i], slots[i + 1]) { Bracket = "lower" });
                advanced.Add(new(null, id));
            }
            rounds.Add(new(lowerNumber, matches.AsReadOnly()));
            return advanced;
        }
        foreach (var round in upperRounds)
        {
            var dropped = round.Matches.Reverse().Select(match =>
                new TournamentBracketSlot(null, match.Id) { FromLoser = true }).ToArray();
            if (dropped.Length == 0) continue;
            while (lower.Count > dropped.Length) lower = Pair(lower);
            var combined = new List<TournamentBracketSlot>();
            for (var i = 0; i < Math.Max(lower.Count, dropped.Length); i++)
            {
                if (i < dropped.Length) combined.Add(dropped[i]);
                if (i < lower.Count) combined.Add(lower[i]);
            }
            lower = Pair(combined);
        }
        while (lower.Count > 1) lower = Pair(lower);
        var upperFinal = upperRounds.SelectMany(round => round.Matches).Last();
        var final = new TournamentBracketMatch("grand-final", 1, new(null, upperFinal.Id), lower.Single()) { Bracket = "final" };
        rounds.Add(new(1, new[] { final }));
        if (resetFinal)
            rounds.Add(new(2, new[] { new TournamentBracketMatch("grand-final-reset", 2,
                new(null, final.Id), new(null, final.Id) { FromLoser = true }) { Bracket = "final-reset" } }));
        return new(upper.SlotCount, upper.OpeningSlots, rounds.AsReadOnly());
    }

    public static TournamentBracketProgressSnapshot Calculate(IReadOnlyCollection<TournamentBracketEntrant> entrants,
        IReadOnlyCollection<TournamentBracketResult> results, bool resetFinal = true)
    {
        ArgumentNullException.ThrowIfNull(results);
        var plan = Generate(entrants, resetFinal);
        var matches = plan.Rounds.SelectMany(round => round.Matches).ToArray();
        var known = matches.Select(match => match.Id).ToHashSet(StringComparer.Ordinal);
        var submitted = new Dictionary<string, TournamentBracketResult>(StringComparer.Ordinal);
        foreach (var result in results)
            if (result is null || result.MatchId is null || !known.Contains(result.MatchId)
                || !submitted.TryAdd(result.MatchId, result) || string.IsNullOrEmpty(result.WinnerTeamId)
                || result.LoserMaps < 0 || result.WinnerMaps <= result.LoserMaps || result.WinnerMaps > 3)
                throw new ArgumentException("Invalid or duplicate double-elimination result.", nameof(results));
        var winners = new Dictionary<string, string>(StringComparer.Ordinal);
        var losers = new Dictionary<string, string>(StringComparer.Ordinal);
        var progress = new List<TournamentBracketMatchProgress>();
        string? Resolve(TournamentBracketSlot slot) => slot.TeamId ??
            (slot.FromMatchId is { } id && (slot.FromLoser ? losers : winners).TryGetValue(id, out var team) ? team : null);
        string? upperChampion = null;
        string? champion = null;
        foreach (var match in matches)
        {
            var home = Resolve(match.Home);
            var away = Resolve(match.Away);
            var enabled = match.Id != "grand-final-reset" ||
                winners.TryGetValue("grand-final", out var grandWinner) && grandWinner != upperChampion;
            string? winner = null;
            if (submitted.TryGetValue(match.Id, out var result))
            {
                if (!enabled || home is null || away is null || home == away ||
                    result.WinnerTeamId != home && result.WinnerTeamId != away)
                    throw new ArgumentException("Result requires resolved distinct participants and an enabled match.", nameof(results));
                winner = result.WinnerTeamId;
                winners.Add(match.Id, winner);
                losers.Add(match.Id, winner == home ? away : home);
            }
            progress.Add(new(match.Id, match.Round, home, away, winner) { IsEnabled = enabled, Bracket = match.Bracket });
            if (match.Id == "grand-final")
            {
                upperChampion = home;
                if (winner is not null && (!resetFinal || winner == home)) champion = winner;
            }
            if (match.Id == "grand-final-reset" && enabled && winner is not null) champion = winner;
        }
        return new(progress.AsReadOnly(), champion);
    }
}
