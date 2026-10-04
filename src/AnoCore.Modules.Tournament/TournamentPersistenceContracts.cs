namespace AnoCore.Modules.Tournament;

public sealed record TournamentStoredMatch(
    TournamentMatchConfiguration Configuration,
    TournamentRecoverySnapshot Snapshot,
    long Revision);

public interface ITournamentMatchRepository
{
    ValueTask<TournamentStoredMatch> StoreAsync(
        TournamentMatchConfiguration configuration,
        TournamentRecoverySnapshot snapshot,
        bool makeActive,
        CancellationToken cancellationToken = default);

    ValueTask<TournamentStoredMatch?> LoadAsync(
        Guid matchId,
        CancellationToken cancellationToken = default);

    ValueTask<TournamentStoredMatch?> LoadActiveAsync(
        CancellationToken cancellationToken = default);

    ValueTask<TournamentStoredMatch> SaveSnapshotAsync(
        Guid matchId,
        long expectedRevision,
        TournamentRecoverySnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask<TournamentStoredMatch> DeactivateAsync(
        Guid matchId,
        long expectedRevision,
        TournamentRecoverySnapshot snapshot,
        CancellationToken cancellationToken = default);
}

public sealed class TournamentConcurrencyException : InvalidOperationException
{
    public TournamentConcurrencyException(Guid matchId, long expectedRevision, long actualRevision)
        : base($"Tournament match {matchId:D} revision changed from {expectedRevision} to {actualRevision}.")
    {
        MatchId = matchId;
        ExpectedRevision = expectedRevision;
        ActualRevision = actualRevision;
    }

    public Guid MatchId { get; }
    public long ExpectedRevision { get; }
    public long ActualRevision { get; }
}

public sealed class TournamentRecoverySession
{
    internal TournamentRecoverySession(TournamentMatchStateMachine machine, long revision)
    {
        Machine = machine ?? throw new ArgumentNullException(nameof(machine));
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        Revision = revision;
    }

    public TournamentMatchStateMachine Machine { get; }
    public long Revision { get; private set; }

    internal void Advance(long revision)
    {
        if (revision <= Revision)
            throw new ArgumentOutOfRangeException(nameof(revision));
        Revision = revision;
    }
}

public sealed class TournamentRecoveryService
{
    private readonly ITournamentMatchRepository _repository;

    public TournamentRecoveryService(ITournamentMatchRepository repository)
        => _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async ValueTask<TournamentRecoverySession> BeginAsync(
        TournamentMatchConfiguration configuration,
        bool makeActive = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var machine = new TournamentMatchStateMachine(configuration);
        var stored = await _repository.StoreAsync(
            configuration, machine.Snapshot(), makeActive, cancellationToken).ConfigureAwait(false);
        return new TournamentRecoverySession(machine, stored.Revision);
    }

    public async ValueTask<TournamentRecoverySession?> RestoreActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var stored = await _repository.LoadActiveAsync(cancellationToken).ConfigureAwait(false);
        if (stored is null) return null;
        var machine = TournamentMatchStateMachine.Restore(stored.Configuration, stored.Snapshot);
        return new TournamentRecoverySession(machine, stored.Revision);
    }

    public async ValueTask SaveAsync(
        TournamentRecoverySession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var stored = await _repository.SaveSnapshotAsync(
            session.Machine.Configuration.MatchId,
            session.Revision,
            session.Machine.Snapshot(),
            cancellationToken).ConfigureAwait(false);
        session.Advance(stored.Revision);
    }

    public async ValueTask DeactivateAsync(
        TournamentRecoverySession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var stored = await _repository.DeactivateAsync(
            session.Machine.Configuration.MatchId,
            session.Revision,
            session.Machine.Snapshot(),
            cancellationToken).ConfigureAwait(false);
        session.Advance(stored.Revision);
    }
}
