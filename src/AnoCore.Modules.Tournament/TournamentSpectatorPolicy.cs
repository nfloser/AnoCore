using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Tournament;

public enum TournamentSpectatorAccessKind
{
    RosterPlayer = 1,
    TeamACoach = 2,
    TeamBCoach = 3,
    WhitelistedSpectator = 4,
    PublicSpectator = 5,
    Denied = 6,
}

public sealed record TournamentSpectatorDecision(TournamentSpectatorAccessKind Kind)
{
    public bool IsRosterPlayer => Kind == TournamentSpectatorAccessKind.RosterPlayer;
    public bool CanSpectate => Kind is
        TournamentSpectatorAccessKind.TeamACoach
        or TournamentSpectatorAccessKind.TeamBCoach
        or TournamentSpectatorAccessKind.WhitelistedSpectator
        or TournamentSpectatorAccessKind.PublicSpectator;
}

public sealed class TournamentSpectatorPolicy
{
    private const int MaxCoachesPerTeam = 4;
    private const int MaxWhitelist = 64;

    private readonly HashSet<PlayerId> _teamACoaches;
    private readonly HashSet<PlayerId> _teamBCoaches;
    private readonly HashSet<PlayerId> _whitelist;

    public TournamentSpectatorPolicy(
        TournamentMatchConfiguration configuration,
        bool allowPublicSpectators = false,
        IEnumerable<PlayerId>? teamACoaches = null,
        IEnumerable<PlayerId>? teamBCoaches = null,
        IEnumerable<PlayerId>? spectatorWhitelist = null)
    {
        Configuration = configuration
            ?? throw new ArgumentNullException(nameof(configuration));
        _teamACoaches = Normalize(teamACoaches, MaxCoachesPerTeam, nameof(teamACoaches));
        _teamBCoaches = Normalize(teamBCoaches, MaxCoachesPerTeam, nameof(teamBCoaches));
        _whitelist = Normalize(spectatorWhitelist, MaxWhitelist, nameof(spectatorWhitelist));

        var roster = Configuration.TeamA.Members
            .Concat(Configuration.TeamB.Members)
            .ToHashSet();
        if (_teamACoaches.Overlaps(roster)
            || _teamBCoaches.Overlaps(roster)
            || _whitelist.Overlaps(roster))
        {
            throw new ArgumentException(
                "Roster players cannot also be configured as coaches or spectators.");
        }

        if (_teamACoaches.Overlaps(_teamBCoaches))
            throw new ArgumentException("A player cannot coach both tournament teams.");
        if (_whitelist.Overlaps(_teamACoaches)
            || _whitelist.Overlaps(_teamBCoaches))
        {
            throw new ArgumentException(
                "Coaches do not need a duplicate spectator whitelist entry.");
        }

        AllowPublicSpectators = allowPublicSpectators;
    }

    public TournamentMatchConfiguration Configuration { get; }
    public Guid MatchId => Configuration.MatchId;
    public bool AllowPublicSpectators { get; }
    public IReadOnlyCollection<PlayerId> TeamACoaches => _teamACoaches.ToArray();
    public IReadOnlyCollection<PlayerId> TeamBCoaches => _teamBCoaches.ToArray();
    public IReadOnlyCollection<PlayerId> SpectatorWhitelist => _whitelist.ToArray();

    public TournamentSpectatorDecision Decide(PlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(playerId);

        var rosterSide = Configuration.TeamFor(playerId);
        if (rosterSide is not null)
        {
            return new TournamentSpectatorDecision(
                TournamentSpectatorAccessKind.RosterPlayer);
        }

        if (_teamACoaches.Contains(playerId))
            return new TournamentSpectatorDecision(TournamentSpectatorAccessKind.TeamACoach);
        if (_teamBCoaches.Contains(playerId))
            return new TournamentSpectatorDecision(TournamentSpectatorAccessKind.TeamBCoach);
        if (_whitelist.Contains(playerId))
            return new TournamentSpectatorDecision(
                TournamentSpectatorAccessKind.WhitelistedSpectator);
        return new TournamentSpectatorDecision(
            AllowPublicSpectators
                ? TournamentSpectatorAccessKind.PublicSpectator
                : TournamentSpectatorAccessKind.Denied);
    }

    private static HashSet<PlayerId> Normalize(
        IEnumerable<PlayerId>? values,
        int maximum,
        string parameterName)
    {
        if (values is null)
            return [];

        var result = values.ToHashSet();
        if (result.Any(value => value is null))
            throw new ArgumentException("Player ids cannot be null.", parameterName);
        if (result.Count > maximum)
            throw new ArgumentException(
                $"At most {maximum} player ids may be configured.", parameterName);
        return result;
    }
}

public sealed record TournamentSpectatorPolicySnapshot(
    long Version,
    TournamentSpectatorPolicy? Policy,
    CancellationToken Lifetime);

public interface ITournamentSpectatorPolicySource
{
    TournamentSpectatorPolicySnapshot Read();
}

public sealed class TournamentSpectatorPolicySource : ITournamentSpectatorPolicySource, IDisposable
{
    private readonly object _gate = new();
    private TournamentSpectatorPolicy? _current;
    private CancellationTokenSource _lifetime = new();
    private long _version;
    private int _disposed;

    public TournamentSpectatorPolicySnapshot Read()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return new TournamentSpectatorPolicySnapshot(
                _version, _current, _lifetime.Token);
        }
    }

    public long Replace(TournamentSpectatorPolicy? policy)
    {
        CancellationTokenSource previous;
        long version;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            previous = _lifetime;
            _lifetime = new CancellationTokenSource();
            _current = policy;
            version = ++_version;
        }

        previous.Cancel();
        previous.Dispose();
        return version;
    }

    public void Dispose()
    {
        CancellationTokenSource lifetime;
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            lifetime = _lifetime;
            _current = null;
            _version++;
        }

        lifetime.Cancel();
        lifetime.Dispose();
    }
}
