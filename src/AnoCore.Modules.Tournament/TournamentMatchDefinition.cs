using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Tournament;

public sealed class TournamentMatchDefinition
{
    public bool Enabled { get; set; }
    public string MatchId { get; set; } = string.Empty;
    public int BestOf { get; set; } = 1;
    public bool KnifeRound { get; set; } = true;
    public bool OvertimeEnabled { get; set; } = true;
    public TournamentTeamDefinition TeamA { get; set; } = new();
    public TournamentTeamDefinition TeamB { get; set; } = new();

    public static TournamentMatchDefinition Default => new();

    public static IReadOnlyCollection<string> Validate(TournamentMatchDefinition definition)
    {
        if (definition is null) return ["Tournament match definition is required."];
        if (!definition.Enabled) return [];

        var errors = new List<string>();
        if (!Guid.TryParse(definition.MatchId, out var matchId) || matchId == Guid.Empty)
            errors.Add("MatchId must be a non-empty GUID.");
        if (definition.BestOf is not (1 or 3 or 5))
            errors.Add("BestOf must be 1, 3 or 5.");

        ValidateTeam(definition.TeamA, "TeamA", errors);
        ValidateTeam(definition.TeamB, "TeamB", errors);

        if (definition.TeamA?.Members is not null
            && definition.TeamB?.Members is not null)
        {
            var overlap = definition.TeamA.Members
                .Where(value => value != 0)
                .Intersect(definition.TeamB.Members)
                .FirstOrDefault();
            if (overlap != 0)
                errors.Add("A SteamID cannot belong to both tournament teams.");
        }

        return errors;
    }

    public TournamentMatchConfiguration ToConfiguration()
    {
        var errors = Validate(this);
        if (errors.Count != 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(TournamentMatchDefinition));
        if (!Enabled)
            throw new InvalidOperationException("The tournament match definition is disabled.");

        var bestOf = BestOf switch
        {
            1 => TournamentBestOf.One,
            3 => TournamentBestOf.Three,
            5 => TournamentBestOf.Five,
            _ => throw new InvalidOperationException("Validated best-of value is unsupported."),
        };

        return new TournamentMatchConfiguration(
            Guid.Parse(MatchId),
            bestOf,
            ToTeam(TeamA),
            ToTeam(TeamB),
            KnifeRound,
            OvertimeEnabled);
    }

    private static TournamentTeam ToTeam(TournamentTeamDefinition value)
        => new(
            value.Name,
            value.Tag,
            new PlayerId(value.CaptainSteamId),
            value.Members.Select(member => new PlayerId(member)));

    private static void ValidateTeam(
        TournamentTeamDefinition? team,
        string label,
        ICollection<string> errors)
    {
        if (team is null)
        {
            errors.Add($"{label} is required.");
            return;
        }

        if (!Printable(team.Name, 1, 64))
            errors.Add($"{label}.Name must contain 1-64 printable characters.");
        if (!Printable(team.Tag, 1, 12))
            errors.Add($"{label}.Tag must contain 1-12 printable characters.");
        if (team.CaptainSteamId == 0)
            errors.Add($"{label}.CaptainSteamId must be non-zero.");
        if (team.Members is null || team.Members.Count is < 1 or > 10)
        {
            errors.Add($"{label}.Members must contain 1-10 SteamID64 values.");
            return;
        }

        if (team.Members.Any(value => value == 0))
            errors.Add($"{label}.Members cannot contain SteamID64 0.");
        if (team.Members.Distinct().Count() != team.Members.Count)
            errors.Add($"{label}.Members must be unique.");
        if (!team.Members.Contains(team.CaptainSteamId))
            errors.Add($"{label}.CaptainSteamId must be included in Members.");
    }

    private static bool Printable(string? value, int minimum, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        return trimmed.Length >= minimum
            && trimmed.Length <= maximum
            && trimmed.All(character => !char.IsControl(character));
    }
}

public sealed class TournamentTeamDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public ulong CaptainSteamId { get; set; }
    public List<ulong> Members { get; set; } = [];
}
