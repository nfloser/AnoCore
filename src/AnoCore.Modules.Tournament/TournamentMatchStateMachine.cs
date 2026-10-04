using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Tournament;

public sealed class TournamentMatchStateMachine
{
    private readonly HashSet<PlayerId> _ready = [];
    private readonly List<string> _maps = [];
    private TournamentMatchState? _resumeState;

    public TournamentMatchStateMachine(TournamentMatchConfiguration configuration)
        => Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    public TournamentMatchConfiguration Configuration { get; }
    public TournamentMatchState State { get; private set; } = TournamentMatchState.Setup;
    public IReadOnlyList<string> Maps => _maps;
    public int CurrentMapIndex { get; private set; }
    public int TeamAMaps { get; private set; }
    public int TeamBMaps { get; private set; }
    public TournamentTeamSlot? KnifeWinner { get; private set; }
    public TournamentTeamSlot? SideChooser { get; private set; }
    public PlayerTeam TeamASide { get; private set; } = PlayerTeam.Terrorist;
    public PlayerTeam TeamBSide { get; private set; } = PlayerTeam.CounterTerrorist;

    public PlayerTeam? AssignedSide(PlayerId playerId)
        => Configuration.TeamFor(playerId) switch
        {
            TournamentTeamSlot.TeamA => TeamASide,
            TournamentTeamSlot.TeamB => TeamBSide,
            _ => null,
        };

    public void OpenReady()
    {
        Require(TournamentMatchState.Setup);
        State = TournamentMatchState.Ready;
    }

    public bool Ready(PlayerId playerId)
    {
        Require(TournamentMatchState.Ready);
        if (Configuration.TeamFor(playerId) is null)
            throw new InvalidOperationException("Only rostered players can ready.");
        _ready.Add(playerId);
        return IsReady;
    }

    public bool IsReady =>
        Configuration.TeamA.Members.All(_ready.Contains)
        && Configuration.TeamB.Members.All(_ready.Contains);

    public void BeginVeto()
    {
        Require(TournamentMatchState.Ready);
        if (!IsReady)
            throw new InvalidOperationException("Both tournament rosters must be ready before veto.");
        State = TournamentMatchState.Veto;
    }

    public void CompleteVeto(IEnumerable<string> maps)
    {
        Require(TournamentMatchState.Veto);
        ArgumentNullException.ThrowIfNull(maps);
        var selected = maps.Select(NormalizeMap).ToArray();
        if (selected.Length != (int)Configuration.BestOf)
            throw new ArgumentException($"A BO{(int)Configuration.BestOf} requires exactly {(int)Configuration.BestOf} selected map(s).", nameof(maps));
        if (selected.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length)
            throw new ArgumentException("Selected tournament maps must be unique.", nameof(maps));
        _maps.Clear();
        _maps.AddRange(selected);
        State = Configuration.KnifeRound
            ? TournamentMatchState.Knife
            : TournamentMatchState.Live;
    }

    public void CompleteKnife(TournamentTeamSlot winner)
    {
        Require(TournamentMatchState.Knife);
        ValidateTeam(winner);
        KnifeWinner = winner;
        SideChooser = winner;
        State = TournamentMatchState.SideChoice;
    }

    public void ChooseSide(TournamentTeamSlot chooser, PlayerTeam side)
    {
        Require(TournamentMatchState.SideChoice);
        ValidateTeam(chooser);
        if (SideChooser != chooser)
            throw new InvalidOperationException("Only the knife-round winner can choose the starting side.");
        if (side is not (PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist))
            throw new ArgumentOutOfRangeException(nameof(side));
        if (chooser == TournamentTeamSlot.TeamA)
        {
            TeamASide = side;
            TeamBSide = Opposite(side);
        }
        else
        {
            TeamBSide = side;
            TeamASide = Opposite(side);
        }
        State = TournamentMatchState.Live;
    }

    public void Pause()
    {
        if (State is not (TournamentMatchState.Live or TournamentMatchState.Overtime))
            throw new InvalidOperationException($"Cannot pause a match in {State}.");
        _resumeState = State;
        State = TournamentMatchState.Paused;
    }

    public void Resume()
    {
        Require(TournamentMatchState.Paused);
        State = _resumeState ?? throw new InvalidOperationException("Pause resume state is missing.");
        _resumeState = null;
    }

    public void EnterOvertime()
    {
        Require(TournamentMatchState.Live);
        if (!Configuration.OvertimeEnabled)
            throw new InvalidOperationException("Overtime is disabled for this match.");
        State = TournamentMatchState.Overtime;
    }

    public bool CompleteMap(TournamentTeamSlot winner)
    {
        if (State is not (TournamentMatchState.Live or TournamentMatchState.Overtime))
            throw new InvalidOperationException($"Cannot complete a map in {State}.");
        ValidateTeam(winner);
        if (_maps.Count == 0)
            throw new InvalidOperationException("No tournament maps have been selected.");

        if (winner == TournamentTeamSlot.TeamA) TeamAMaps++;
        else TeamBMaps++;

        if (TeamAMaps >= Configuration.MapsToWin || TeamBMaps >= Configuration.MapsToWin)
        {
            State = TournamentMatchState.Completed;
            return true;
        }

        CurrentMapIndex++;
        if (CurrentMapIndex >= _maps.Count)
            throw new InvalidOperationException("The configured series has no map remaining.");
        KnifeWinner = null;
        SideChooser = null;
        State = Configuration.KnifeRound
            ? TournamentMatchState.Knife
            : TournamentMatchState.Live;
        return false;
    }

    public TournamentRecoverySnapshot Snapshot()
        => new(
            Configuration.MatchId,
            State,
            _maps.ToArray(),
            CurrentMapIndex,
            TeamAMaps,
            TeamBMaps,
            _ready.OrderBy(x => x.SteamId64).ToArray(),
            KnifeWinner,
            SideChooser,
            TeamASide,
            TeamBSide,
            _resumeState);

    public static TournamentMatchStateMachine Restore(
        TournamentMatchConfiguration configuration,
        TournamentRecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.MatchId != configuration.MatchId)
            throw new ArgumentException("Snapshot match id does not match configuration.", nameof(snapshot));
        if (!Enum.IsDefined(snapshot.State))
            throw new ArgumentException("Snapshot contains an unsupported state.", nameof(snapshot));
        if (snapshot.CurrentMapIndex < 0
            || snapshot.TeamAMaps < 0
            || snapshot.TeamBMaps < 0)
            throw new ArgumentException("Snapshot counters cannot be negative.", nameof(snapshot));

        var machine = new TournamentMatchStateMachine(configuration)
        {
            State = snapshot.State,
            CurrentMapIndex = snapshot.CurrentMapIndex,
            TeamAMaps = snapshot.TeamAMaps,
            TeamBMaps = snapshot.TeamBMaps,
            KnifeWinner = snapshot.KnifeWinner,
            SideChooser = snapshot.SideChooser,
            TeamASide = snapshot.TeamASide,
            TeamBSide = snapshot.TeamBSide,
            _resumeState = snapshot.ResumeState,
        };
        machine._maps.AddRange(snapshot.Maps.Select(NormalizeMap));
        foreach (var player in snapshot.ReadyPlayers)
        {
            if (configuration.TeamFor(player) is null)
                throw new ArgumentException("Snapshot contains a ready player outside the roster.", nameof(snapshot));
            machine._ready.Add(player);
        }
        machine.ValidateSnapshot();
        return machine;
    }

    private void ValidateSnapshot()
    {
        if (TeamASide is not (PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist)
            || TeamBSide != Opposite(TeamASide))
            throw new ArgumentException("Snapshot team sides are invalid.");
        if (KnifeWinner is not null && !Enum.IsDefined(KnifeWinner.Value))
            throw new ArgumentException("Snapshot knife winner is invalid.");
        if (SideChooser is not null && !Enum.IsDefined(SideChooser.Value))
            throw new ArgumentException("Snapshot side chooser is invalid.");
        if (_resumeState is not null && !Enum.IsDefined(_resumeState.Value))
            throw new ArgumentException("Snapshot resume state is invalid.");

        var requiresReady = State is >= TournamentMatchState.Veto;
        if (requiresReady && !IsReady)
            throw new ArgumentException("A veto or running snapshot requires both rosters to be ready.");
        if (State == TournamentMatchState.Setup && _ready.Count != 0)
            throw new ArgumentException("Setup snapshots cannot contain ready players.");

        if (State is TournamentMatchState.Setup or TournamentMatchState.Ready or TournamentMatchState.Veto)
        {
            if (_maps.Count != 0)
                throw new ArgumentException("Pre-veto-completion snapshots cannot contain selected maps.");
        }

        if (!Configuration.KnifeRound)
        {
            if (State is TournamentMatchState.Knife or TournamentMatchState.SideChoice)
                throw new ArgumentException("Knife states are invalid when knife rounds are disabled.");
            if (KnifeWinner is not null || SideChooser is not null)
                throw new ArgumentException("Knife ownership is invalid when knife rounds are disabled.");
        }
        else if (State == TournamentMatchState.SideChoice)
        {
            if (KnifeWinner is null || SideChooser is null || KnifeWinner != SideChooser)
                throw new ArgumentException("Side choice requires the knife winner to own the choice.");
        }
        else if (State == TournamentMatchState.Knife
                 && (KnifeWinner is not null || SideChooser is not null))
        {
            throw new ArgumentException("An unfinished knife round cannot already have a winner.");
        }
        if (_maps.Count > 0 && _maps.Count != (int)Configuration.BestOf)
            throw new ArgumentException("Snapshot map count does not match the configured series.");
        if (_maps.Count == 0 && State is >= TournamentMatchState.Knife)
            throw new ArgumentException("A running snapshot requires selected maps.");
        if (_maps.Count > 0 && CurrentMapIndex >= _maps.Count)
            throw new ArgumentException("Snapshot current map index is outside the selected series.");
        var playedMaps = TeamAMaps + TeamBMaps;
        if (playedMaps > (int)Configuration.BestOf)
            throw new ArgumentException("Snapshot series score exceeds the configured best-of.");
        var teamACompleted = TeamAMaps >= Configuration.MapsToWin;
        var teamBCompleted = TeamBMaps >= Configuration.MapsToWin;
        if (teamACompleted && teamBCompleted)
            throw new ArgumentException("Both teams cannot have a winning series score.");
        if (State == TournamentMatchState.Completed)
        {
            if (!teamACompleted && !teamBCompleted)
                throw new ArgumentException("Completed snapshot has no series winner.");
            if (playedMaps == 0 || CurrentMapIndex != playedMaps - 1)
                throw new ArgumentException("Completed snapshot map index does not match its series score.");
        }
        else
        {
            if (teamACompleted || teamBCompleted)
                throw new ArgumentException("A non-completed snapshot cannot already contain a series winner.");
            if (CurrentMapIndex != playedMaps)
                throw new ArgumentException("Snapshot map index does not match its series score.");
        }
        if (State == TournamentMatchState.Paused)
        {
            if (_resumeState is not (TournamentMatchState.Live or TournamentMatchState.Overtime))
                throw new ArgumentException("Paused snapshot must carry a live resume state.");
        }
        else if (_resumeState is not null)
        {
            throw new ArgumentException("Only paused snapshots may carry a resume state.");
        }
    }

    private void Require(TournamentMatchState expected)
    {
        if (State != expected)
            throw new InvalidOperationException($"Expected {expected}, current state is {State}.");
    }

    private static void ValidateTeam(TournamentTeamSlot team)
    {
        if (!Enum.IsDefined(team))
            throw new ArgumentOutOfRangeException(nameof(team));
    }

    private static PlayerTeam Opposite(PlayerTeam side)
        => side == PlayerTeam.Terrorist
            ? PlayerTeam.CounterTerrorist
            : PlayerTeam.Terrorist;

    private static string NormalizeMap(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Tournament map names cannot be empty.", nameof(value));
        var normalized = value.Trim();
        if (normalized.Length > 128 || normalized.Any(char.IsControl))
            throw new ArgumentException("Tournament map names are invalid.", nameof(value));
        return normalized;
    }
}
