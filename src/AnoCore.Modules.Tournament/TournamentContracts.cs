using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Tournament;

public enum TournamentBestOf
{
    One = 1,
    Three = 3,
    Five = 5,
}

public enum TournamentMatchState
{
    Setup = 0,
    Ready = 1,
    Veto = 2,
    Knife = 3,
    SideChoice = 4,
    Live = 5,
    Paused = 6,
    Overtime = 7,
    Completed = 8,
}

public enum TournamentTeamSlot
{
    TeamA = 1,
    TeamB = 2,
}

public sealed record TournamentTeam
{
    public TournamentTeam(string name, string tag, PlayerId captain,
        IEnumerable<PlayerId> members)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 64)
            throw new ArgumentException("Tournament team names must contain 1-64 characters.", nameof(name));
        if (string.IsNullOrWhiteSpace(tag) || tag.Trim().Length > 12)
            throw new ArgumentException("Tournament team tags must contain 1-12 characters.", nameof(tag));
        Captain = captain ?? throw new ArgumentNullException(nameof(captain));
        ArgumentNullException.ThrowIfNull(members);
        var values = members.Distinct().OrderBy(x => x.SteamId64).ToArray();
        if (values.Length is < 1 or > 10)
            throw new ArgumentException("Tournament teams require 1-10 unique members.", nameof(members));
        if (!values.Contains(Captain))
            throw new ArgumentException("The captain must be a team member.", nameof(captain));

        Name = name.Trim();
        Tag = tag.Trim();
        Members = values;
    }

    public string Name { get; }
    public string Tag { get; }
    public PlayerId Captain { get; }
    public IReadOnlyList<PlayerId> Members { get; }
}

public sealed record TournamentMatchConfiguration
{
    public TournamentMatchConfiguration(Guid matchId, TournamentBestOf bestOf,
        TournamentTeam teamA, TournamentTeam teamB, bool knifeRound = true,
        bool overtimeEnabled = true)
    {
        if (matchId == Guid.Empty)
            throw new ArgumentException("A match id is required.", nameof(matchId));
        if (!Enum.IsDefined(bestOf))
            throw new ArgumentOutOfRangeException(nameof(bestOf));
        TeamA = teamA ?? throw new ArgumentNullException(nameof(teamA));
        TeamB = teamB ?? throw new ArgumentNullException(nameof(teamB));
        if (TeamA.Members.Intersect(TeamB.Members).Any())
            throw new ArgumentException("A player cannot belong to both tournament teams.");

        MatchId = matchId;
        BestOf = bestOf;
        KnifeRound = knifeRound;
        OvertimeEnabled = overtimeEnabled;
    }

    public Guid MatchId { get; }
    public TournamentBestOf BestOf { get; }
    public TournamentTeam TeamA { get; }
    public TournamentTeam TeamB { get; }
    public bool KnifeRound { get; }
    public bool OvertimeEnabled { get; }
    public int MapsToWin => ((int)BestOf / 2) + 1;

    public TournamentTeamSlot? TeamFor(PlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        if (TeamA.Members.Contains(playerId)) return TournamentTeamSlot.TeamA;
        if (TeamB.Members.Contains(playerId)) return TournamentTeamSlot.TeamB;
        return null;
    }
}

public sealed record TournamentRecoverySnapshot(
    Guid MatchId,
    TournamentMatchState State,
    IReadOnlyList<string> Maps,
    int CurrentMapIndex,
    int TeamAMaps,
    int TeamBMaps,
    IReadOnlyCollection<PlayerId> ReadyPlayers,
    TournamentTeamSlot? KnifeWinner,
    TournamentTeamSlot? SideChooser,
    PlayerTeam TeamASide,
    PlayerTeam TeamBSide,
    TournamentMatchState? ResumeState);
