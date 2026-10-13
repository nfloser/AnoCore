namespace AnoCore.Modules.Tournament;

/// <summary>
/// A tournament event is a multi-team plan. It is deliberately separate from
/// TournamentMatchDefinition, which owns one live two-team CS2 match.
/// </summary>
public sealed class TournamentEventDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TeamSize { get; set; } = 5;
    public int MaxSubstitutes { get; set; } = 2;
    public List<TournamentEventTeamDefinition> Teams { get; set; } = [];
    public List<TournamentGroupDefinition> Groups { get; set; } = [];
    public TournamentStandingsRules Standings { get; set; } = new();

    public static IReadOnlyCollection<string> Validate(TournamentEventDefinition? definition)
    {
        if (definition is null) return ["Tournament event definition is required."];

        var errors = new List<string>();
        if (!ValidId(definition.Id))
            errors.Add("Tournament Id must be a 1-48 character lowercase slug.");
        if (!ValidText(definition.Name, 1, 80))
            errors.Add("Tournament Name must be printable and 1-80 characters.");
        if (definition.TeamSize != 5)
            errors.Add("Tournament TeamSize must be 5 for the current CS2 5v5 format.");
        if (definition.MaxSubstitutes is < 0 or > 5)
            errors.Add("MaxSubstitutes must be between 0 and 5.");

        var teams = definition.Teams ?? [];
        if (teams.Count is < 3 or > 10)
            errors.Add("Tournament requires 3-10 teams.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedPlayers = new HashSet<ulong>();
        var seeds = new HashSet<int>();

        foreach (var team in teams)
        {
            if (team is null)
            {
                errors.Add("Tournament team cannot be null.");
                continue;
            }
            if (!ValidId(team.Id) || !ids.Add(team.Id))
                errors.Add($"Invalid or duplicate team ID: '{team.Id}'.");
            if (!ValidText(team.Name, 1, 64) || !names.Add(team.Name.Trim()))
                errors.Add($"Invalid or duplicate team name: '{team.Name}'.");
            if (!ValidText(team.Tag, 1, 12))
                errors.Add($"Team '{team.Id}' has an invalid tag.");
            if (team.InitialSeed is { } seed && (seed < 1 || seed > teams.Count || !seeds.Add(seed)))
                errors.Add($"Team '{team.Id}' has a duplicate or out-of-range initial seed.");

            if (team.Starters is null || team.Starters.Count != definition.TeamSize)
                errors.Add($"Team '{team.Id}' must have exactly {definition.TeamSize} starters.");
            if (team.Substitutes is null || team.Substitutes.Count > definition.MaxSubstitutes)
                errors.Add($"Team '{team.Id}' exceeds MaxSubstitutes or has a null substitute list.");
            var players = (team.Starters ?? []).Concat(team.Substitutes ?? []).ToList();
            if (team.CaptainSteamId == 0 || !(team.Starters ?? []).Contains(team.CaptainSteamId))
                errors.Add($"Team '{team.Id}' captain must be one of the starters.");
            foreach (var steamId in players)
            {
                if (steamId == 0 || !usedPlayers.Add(steamId))
                    errors.Add($"Duplicate or zero SteamID in tournament roster: {steamId}.");
            }
        }

        if (definition.Groups is null)
        {
            errors.Add("Groups must not be null.");
        }
        else if (definition.Groups.Count > 0)
        {
            var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in definition.Groups)
            {
                if (group is null)
                {
                    errors.Add("Tournament group cannot be null.");
                    continue;
                }
                if (!ValidId(group.Id) || !groupIds.Add(group.Id))
                    errors.Add($"Invalid or duplicate group ID: '{group.Id}'.");
                if (!ValidText(group.Name, 1, 64))
                    errors.Add($"Group '{group.Id}' must have a printable name.");
                if (group.TeamIds is null || group.TeamIds.Count < 2)
                {
                    errors.Add($"Group '{group.Id}' needs at least 2 teams.");
                    continue;
                }
                foreach (var id in group.TeamIds)
                {
                    if (string.IsNullOrWhiteSpace(id) || !ids.Contains(id) || !assigned.Add(id))
                        errors.Add($"Unknown or multiply assigned team '{id}' in group '{group.Id}'.");
                }
            }
            if (assigned.Count != teams.Count)
                errors.Add("Every tournament team must belong to exactly one group.");
        }

        if (definition.Standings is null)
        {
            errors.Add("Standings rules must not be null.");
        }
        else
        {
            var rules = definition.Standings;
            if (rules.WinPoints is < 0 or > 30 || rules.DrawPoints is < 0 or > 30
                || rules.LossPoints is < 0 or > 30)
                errors.Add("Standings points must be between 0 and 30.");
            if (rules.Tiebreakers is null || rules.Tiebreakers.Distinct().Count() != rules.Tiebreakers.Count
                || rules.Tiebreakers.Any(value => !Enum.IsDefined(value)))
                errors.Add("Tiebreakers must be distinct supported values.");
        }
        return errors;
    }

    internal static TournamentGroupDefinition ResolveGroup(TournamentEventDefinition definition, string id)
    {
        var groups = definition.Groups.Count == 0
            ? [new TournamentGroupDefinition
            {
                Id = "main", Name = "Main", TeamIds = definition.Teams.Select(team => team.Id).ToList(),
            }]
            : definition.Groups;
        return groups.FirstOrDefault(group => string.Equals(group.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown tournament group '{id}'.", nameof(id));
    }

    internal static void RequireValid(TournamentEventDefinition definition)
    {
        var errors = Validate(definition);
        if (errors.Count != 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(definition));
    }

    private static bool ValidId(string? value)
        => !string.IsNullOrEmpty(value) && value.Length <= 48
            && value.All(character => (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9') || character is '-' or '_')
            && value[0] is not ('-' or '_');

    private static bool ValidText(string? value, int min, int max)
        => !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= min
            && value.Trim().Length <= max && value.All(character => !char.IsControl(character));
}

public sealed class TournamentEventTeamDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public ulong CaptainSteamId { get; set; }
    public List<ulong> Starters { get; set; } = [];
    public List<ulong> Substitutes { get; set; } = [];
    public int? InitialSeed { get; set; }
}

public sealed class TournamentGroupDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> TeamIds { get; set; } = [];
}

public enum TournamentTiebreaker
{
    MapDifference,
    RoundDifference,
    MapsWon,
    RoundsWon,
    InitialSeed,
}

public sealed class TournamentStandingsRules
{
    public int WinPoints { get; set; } = 3;
    public int DrawPoints { get; set; } = 1;
    public int LossPoints { get; set; }
    public bool AllowDraws { get; set; }
    public List<TournamentTiebreaker> Tiebreakers { get; set; } =
    [
        TournamentTiebreaker.MapDifference,
        TournamentTiebreaker.RoundDifference,
        TournamentTiebreaker.InitialSeed,
    ];
}

/// <summary>A stable fixture reference; results refer to this ID, never team display names.</summary>
public sealed record TournamentGroupFixture(
    string Id, string GroupId, int Round, string HomeTeamId, string AwayTeamId);

public sealed record TournamentGroupRound(
    int Number, IReadOnlyList<TournamentGroupFixture> Fixtures, string? ByeTeamId);

public static class TournamentRoundRobin
{
    public static IReadOnlyList<TournamentGroupRound> Generate(
        TournamentEventDefinition definition, string groupId, int legs = 1)
    {
        TournamentEventDefinition.RequireValid(definition);
        if (legs is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(legs), "Only one or two legs are supported.");
        var group = TournamentEventDefinition.ResolveGroup(definition, groupId);
        var slots = group.TeamIds.Cast<string?>().ToList();
        if (slots.Count % 2 != 0) slots.Add(null);

        var rounds = new List<TournamentGroupRound>();
        var perLeg = slots.Count - 1;
        for (var leg = 0; leg < legs; leg++)
        {
            for (var round = 0; round < perLeg; round++)
            {
                var fixtures = new List<TournamentGroupFixture>();
                string? bye = null;
                for (var i = 0; i < slots.Count / 2; i++)
                {
                    var a = slots[i];
                    var b = slots[slots.Count - 1 - i];
                    if (a is null || b is null)
                    {
                        bye = a ?? b;
                        continue;
                    }
                    var home = leg == 0 ? a : b;
                    var away = leg == 0 ? b : a;
                    var number = leg * perLeg + round + 1;
                    fixtures.Add(new TournamentGroupFixture(
                        $"{group.Id}:r{number}:m{fixtures.Count + 1}",
                        group.Id, number, home, away));
                }
                rounds.Add(new TournamentGroupRound(leg * perLeg + round + 1,
                    fixtures.AsReadOnly(), bye));

                // Circle method: keep the first slot fixed, rotate all remaining slots.
                var last = slots[^1];
                slots.RemoveAt(slots.Count - 1);
                slots.Insert(1, last);
            }
        }
        return rounds.AsReadOnly();
    }
}

/// <summary>A confirmed map-series result for a generated group fixture.</summary>
public sealed record TournamentGroupResult(
    string FixtureId, int HomeMaps, int AwayMaps, int HomeRounds, int AwayRounds);

public sealed record TournamentTeamStanding(
    string TeamId,
    int GamesPlayed,
    int Wins,
    int Draws,
    int Losses,
    int Points,
    int MapsFor,
    int MapsAgainst,
    int RoundsFor,
    int RoundsAgainst,
    int PointsBehindLeader)
{
    public int MapDifference => MapsFor - MapsAgainst;
    public int RoundDifference => RoundsFor - RoundsAgainst;
}

public static class TournamentStandings
{
    public static IReadOnlyList<TournamentTeamStanding> Calculate(
        TournamentEventDefinition definition, string groupId,
        IReadOnlyCollection<TournamentGroupResult> results, int legs = 1)
    {
        ArgumentNullException.ThrowIfNull(results);
        TournamentEventDefinition.RequireValid(definition);
        var fixtures = TournamentRoundRobin.Generate(definition, groupId, legs)
            .SelectMany(round => round.Fixtures)
            .ToDictionary(fixture => fixture.Id, StringComparer.Ordinal);
        var group = TournamentEventDefinition.ResolveGroup(definition, groupId);
        var stats = group.TeamIds.ToDictionary(id => id, _ => new MutableStanding(),
            StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rules = definition.Standings;

        foreach (var result in results)
        {
            if (result is null || !seen.Add(result.FixtureId) ||
                !fixtures.TryGetValue(result.FixtureId, out var fixture))
                throw new ArgumentException("Unknown, null or duplicate confirmed fixture result.", nameof(results));
            if (result.HomeMaps is < 0 or > 5 || result.AwayMaps is < 0 or > 5 ||
                result.HomeRounds < 0 || result.AwayRounds < 0 ||
                result.HomeMaps + result.AwayMaps == 0 ||
                (!rules.AllowDraws && result.HomeMaps == result.AwayMaps))
                throw new ArgumentException("Invalid or drawn tournament match result.", nameof(results));

            var home = stats[fixture.HomeTeamId];
            var away = stats[fixture.AwayTeamId];
            home.Games++;
            away.Games++;
            home.MapsFor += result.HomeMaps;
            home.MapsAgainst += result.AwayMaps;
            home.RoundsFor += result.HomeRounds;
            home.RoundsAgainst += result.AwayRounds;
            away.MapsFor += result.AwayMaps;
            away.MapsAgainst += result.HomeMaps;
            away.RoundsFor += result.AwayRounds;
            away.RoundsAgainst += result.HomeRounds;

            if (result.HomeMaps == result.AwayMaps)
            {
                home.Draws++;
                away.Draws++;
                home.Points += rules.DrawPoints;
                away.Points += rules.DrawPoints;
            }
            else
            {
                var winner = result.HomeMaps > result.AwayMaps ? home : away;
                var loser = ReferenceEquals(winner, home) ? away : home;
                winner.Wins++;
                loser.Losses++;
                winner.Points += rules.WinPoints;
                loser.Points += rules.LossPoints;
            }
        }

        var teams = definition.Teams.ToDictionary(team => team.Id, StringComparer.OrdinalIgnoreCase);
        var rows = stats.Select(pair => new TournamentTeamStanding(
            pair.Key, pair.Value.Games, pair.Value.Wins, pair.Value.Draws,
            pair.Value.Losses, pair.Value.Points, pair.Value.MapsFor, pair.Value.MapsAgainst,
            pair.Value.RoundsFor, pair.Value.RoundsAgainst, 0)).ToList();
        rows.Sort((a, b) =>
        {
            var comparison = b.Points.CompareTo(a.Points);
            if (comparison != 0) return comparison;
            foreach (var criterion in rules.Tiebreakers)
            {
                comparison = criterion switch
                {
                    TournamentTiebreaker.MapDifference => b.MapDifference.CompareTo(a.MapDifference),
                    TournamentTiebreaker.RoundDifference => b.RoundDifference.CompareTo(a.RoundDifference),
                    TournamentTiebreaker.MapsWon => b.MapsFor.CompareTo(a.MapsFor),
                    TournamentTiebreaker.RoundsWon => b.RoundsFor.CompareTo(a.RoundsFor),
                    TournamentTiebreaker.InitialSeed => (teams[a.TeamId].InitialSeed ?? int.MaxValue)
                        .CompareTo(teams[b.TeamId].InitialSeed ?? int.MaxValue),
                    _ => throw new ArgumentOutOfRangeException(nameof(criterion)),
                };
                if (comparison != 0) return comparison;
            }
            return StringComparer.OrdinalIgnoreCase.Compare(a.TeamId, b.TeamId);
        });
        var leader = rows.Count == 0 ? 0 : rows[0].Points;
        return rows.Select(row => row with { PointsBehindLeader = leader - row.Points }).ToArray();
    }

    private sealed class MutableStanding
    {
        public int Games;
        public int Wins;
        public int Draws;
        public int Losses;
        public int Points;
        public int MapsFor;
        public int MapsAgainst;
        public int RoundsFor;
        public int RoundsAgainst;
    }
}
