namespace AnoCore.Modules.Tournament;

/// <summary>Confirmed winner and map totals, expressed from winner/loser perspectives.</summary>
public sealed record TournamentBracketResult(string MatchId, string WinnerTeamId, int WinnerMaps, int LoserMaps);

public sealed record TournamentBracketMatchProgress(
    string Id, int Round, string? HomeTeamId, string? AwayTeamId, string? WinnerTeamId)
{
    public bool IsReady => HomeTeamId is not null && AwayTeamId is not null && WinnerTeamId is null;
}

public sealed record TournamentBracketProgressSnapshot(
    IReadOnlyList<TournamentBracketMatchProgress> Matches, string? ChampionTeamId);

/// <summary>Rebuilds static playoff progression from validated results without mutating inputs.</summary>
public static class TournamentBracketProgression
{
    public static TournamentBracketProgressSnapshot Calculate(
        IReadOnlyCollection<TournamentBracketEntrant> entrants,
        IReadOnlyCollection<TournamentBracketResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var plan = TournamentSingleElimination.GenerateFinalStage(entrants);
        var matches = plan.Rounds.SelectMany(round => round.Matches).ToArray();
        var known = matches.Select(match => match.Id).ToHashSet(StringComparer.Ordinal);
        var submitted = new Dictionary<string, TournamentBracketResult>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            if (result is null || result.MatchId is null || !known.Contains(result.MatchId)
                || !submitted.TryAdd(result.MatchId, result)
                || string.IsNullOrEmpty(result.WinnerTeamId) || result.LoserMaps < 0
                || result.WinnerMaps <= result.LoserMaps || result.WinnerMaps > 3)
                throw new ArgumentException("Results need a unique known match, a winning team and decisive BO1/BO3/BO5 map totals.", nameof(results));
        }

        var winners = new Dictionary<string, string>(StringComparer.Ordinal);
        var progress = new List<TournamentBracketMatchProgress>();
        string? Resolve(TournamentBracketSlot slot) => slot.TeamId
            ?? (slot.FromMatchId is not null && winners.TryGetValue(slot.FromMatchId, out var winner) ? winner : null);
        foreach (var match in matches)
        {
            var home = Resolve(match.Home);
            var away = Resolve(match.Away);
            string? winner = null;
            if (submitted.TryGetValue(match.Id, out var result))
            {
                if (home is null || away is null
                    || (result.WinnerTeamId != home && result.WinnerTeamId != away))
                    throw new ArgumentException("A result requires both resolved participants and a winner from that pairing.", nameof(results));
                winner = result.WinnerTeamId;
                winners.Add(match.Id, winner);
            }
            progress.Add(new(match.Id, match.Round, home, away, winner));
        }
        return new(progress.AsReadOnly(), progress[^1].WinnerTeamId);
    }
}
