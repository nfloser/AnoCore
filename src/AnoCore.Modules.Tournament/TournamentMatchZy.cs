using System.Text.Json;

namespace AnoCore.Modules.Tournament;

public sealed record TournamentMatchAssignment(long Id, string EventId, string FixtureId, bool IsPlayoff,
    string HomeTeamId, string AwayTeamId, string ServerId, Guid InstanceId, int BestOf, IReadOnlyList<string> Maps, string State);
public sealed record TournamentMatchClaim(TournamentMatchAssignment Assignment, TournamentEventDefinition Definition, string Token);
public sealed record TournamentMatchZyMap(int Number, string Winner, int HomeRounds, int AwayRounds);
public sealed record TournamentMatchZySeries(string Winner, int HomeMaps, int AwayMaps);
public sealed record TournamentMatchZyCompletion(int HomeMaps, int AwayMaps, int HomeRounds, int AwayRounds);
public sealed class TournamentMatchZyResultState
{
    public List<TournamentMatchZyMap> Maps { get; set; } = [];
    public TournamentMatchZySeries? Series { get; set; }
}

/// <summary>MatchZy 0.8.15 file/event contract. Team identity never derives from CT/T side.</summary>
public static class TournamentMatchZy
{
    public const int MaximumPayloadBytes = 128 * 1024;

    public static string CreateConfiguration(TournamentEventDefinition definition, TournamentMatchAssignment assignment,
        string logUrl, string token)
    {
        TournamentEventDefinition.RequireValid(definition);
        RequireBestOf(assignment.BestOf);
        if (assignment.Id < 1 || assignment.Maps.Count != assignment.BestOf || assignment.Maps.Any(map => !ValidMap(map))
            || assignment.Maps.Distinct(StringComparer.Ordinal).Count() != assignment.Maps.Count)
            throw new ArgumentException("Match needs a positive numeric ID and distinct validated maps for its BestOf.");
        if (!Uri.TryCreate(logUrl, UriKind.Absolute, out var url) || (url.Scheme != "https" && !(url.Scheme == "http" && url.IsLoopback))
            || !string.IsNullOrEmpty(url.UserInfo) || token.Length != 64 || token.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("MatchZy log URL must use HTTPS (or loopback HTTP) and a generated assignment token.");
        object Team(string id)
        {
            var team = definition.Teams.Single(team => team.Id == id);
            return new
            {
                id = team.Id,
                name = team.Name,
                tag = team.Tag,
                players = team.Starters.ToDictionary(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => ""),
            };
        }
        if (assignment.HomeTeamId == assignment.AwayTeamId) throw new ArgumentException("A match requires distinct teams.");
        return JsonSerializer.Serialize(new
        {
            matchid = assignment.Id,
            num_maps = assignment.BestOf,
            players_per_team = 5,
            min_players_to_ready = 5,
            min_spectators_to_ready = 0,
            skip_veto = true,
            maplist = assignment.Maps,
            series_can_clinch = true,
            match_side_type = "standard",
            spectators = new { },
            team1 = Team(assignment.HomeTeamId),
            team2 = Team(assignment.AwayTeamId),
            remote_log_url = logUrl,
            remote_log_header_key = "Authorization",
            remote_log_header_value = "Bearer " + token,
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    public static bool ValidMap(string? map) => !string.IsNullOrEmpty(map) && map.Length <= 128
        && map.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '/' or '-') && !map.StartsWith('/')
        && !map.Contains("//", StringComparison.Ordinal);

    public static TournamentMatchZyResultState Apply(TournamentMatchZyResultState state, string payload, long matchId, int bestOf)
    {
        RequireBestOf(bestOf);
        if (System.Text.Encoding.UTF8.GetByteCount(payload) > MaximumPayloadBytes) throw new ArgumentException("MatchZy payload exceeds the size bound.");
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 24 });
        var json = document.RootElement;
        if (json.GetProperty("matchid").GetInt64() != matchId) throw new ArgumentException("Event does not belong to this assignment.");
        var kind = json.GetProperty("event").GetString();
        var next = new TournamentMatchZyResultState { Maps = state.Maps.ToList(), Series = state.Series };
        if (kind == "map_result")
        {
            var map = new TournamentMatchZyMap(json.GetProperty("map_number").GetInt32(), Winner(json),
                json.GetProperty("team1").GetProperty("score").GetInt32(), json.GetProperty("team2").GetProperty("score").GetInt32());
            if (map.Number < 0 || map.Number >= bestOf || map.HomeRounds is < 0 or > 1000 || map.AwayRounds is < 0 or > 1000
                || map.HomeRounds == map.AwayRounds || (map.Winner == "team1") != (map.HomeRounds > map.AwayRounds))
                throw new ArgumentException("Invalid map ordinal, scores or winning team.");
            var previous = next.Maps.SingleOrDefault(item => item.Number == map.Number);
            if (previous is not null && previous != map) throw new ArgumentException("A map result conflicts with a previously accepted result.");
            if (previous is null) next.Maps.Add(map);
            next.Maps = next.Maps.OrderBy(map => map.Number).ToList();
        }
        else if (kind == "series_end")
        {
            var series = new TournamentMatchZySeries(Winner(json), json.GetProperty("team1_series_score").GetInt32(),
                json.GetProperty("team2_series_score").GetInt32());
            var needed = bestOf / 2 + 1;
            if (Math.Max(series.HomeMaps, series.AwayMaps) != needed || Math.Min(series.HomeMaps, series.AwayMaps) < 0
                || Math.Min(series.HomeMaps, series.AwayMaps) >= needed || (series.Winner == "team1") != (series.HomeMaps > series.AwayMaps))
                throw new ArgumentException("Series result does not complete the configured BestOf.");
            if (next.Series is not null && next.Series != series) throw new ArgumentException("A series result conflicts with a previously accepted result.");
            next.Series = series;
        }
        _ = Completed(next, bestOf);
        return next;
    }

    public static TournamentMatchZyCompletion? Completed(TournamentMatchZyResultState state, int bestOf)
    {
        RequireBestOf(bestOf);
        if (state.Series is not { } series) return null;
        var expected = series.HomeMaps + series.AwayMaps;
        if (state.Maps.Count > expected) throw new ArgumentException("More map results than the declared completed series.");
        if (state.Maps.Count < expected) return null;
        if (!state.Maps.Select(map => map.Number).SequenceEqual(Enumerable.Range(0, expected))
            || state.Maps.Count(map => map.Winner == "team1") != series.HomeMaps
            || state.Maps.Count(map => map.Winner == "team2") != series.AwayMaps)
            throw new ArgumentException("Map results contradict the completed series.");
        return new(series.HomeMaps, series.AwayMaps, state.Maps.Sum(map => map.HomeRounds), state.Maps.Sum(map => map.AwayRounds));
    }

    private static string Winner(JsonElement json)
    {
        var winner = json.GetProperty("winner").GetProperty("team").GetString();
        return winner is "team1" or "team2" ? winner : throw new ArgumentException("Event needs a stable MatchZy team1/team2 winner.");
    }
    private static void RequireBestOf(int bestOf)
    {
        if (bestOf is not (1 or 3 or 5)) throw new ArgumentOutOfRangeException(nameof(bestOf));
    }
}
