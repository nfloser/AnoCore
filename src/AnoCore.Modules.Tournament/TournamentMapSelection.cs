using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Tournament;

public sealed record TournamentMapSelectionRequest(
    Guid MatchId,
    long Revision,
    TournamentBestOf BestOf,
    PlayerId Manager,
    IReadOnlyCollection<PlayerId> EligiblePlayers);

public sealed record TournamentMapSelectionResult(
    Guid MatchId,
    long Revision,
    IReadOnlyList<string> Maps);

public interface ITournamentMapSelectionSource
{
    ValueTask StartAsync(
        TournamentMapSelectionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<TournamentMapSelectionResult?> CompleteAsync(
        PlayerId manager,
        CancellationToken cancellationToken = default);

    ValueTask CancelAsync(
        PlayerId manager,
        CancellationToken cancellationToken = default);
}

public sealed class TournamentMapSelectionService : IDisposable
{
    private readonly TournamentRecoveryService _recovery;
    private readonly TournamentMatchRuntime _runtime;
    private readonly ITournamentMapSelectionSource _source;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    public TournamentMapSelectionService(
        TournamentRecoveryService recovery,
        TournamentMatchRuntime runtime,
        ITournamentMapSelectionSource source)
    {
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public async ValueTask StartAsync(
        PlayerId manager,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var current = _runtime.CurrentSession
                ?? throw new InvalidOperationException("No active tournament match.");
            var machine = current.Machine;
            if (machine.State != TournamentMatchState.Ready || !machine.IsReady)
                throw new InvalidOperationException(
                    "Tournament map selection requires both rosters ready in Ready state.");

            var eligible = machine.Configuration.TeamA.Members
                .Concat(machine.Configuration.TeamB.Members)
                .Distinct()
                .OrderBy(player => player.SteamId64)
                .ToArray();
            await _source.StartAsync(
                new TournamentMapSelectionRequest(
                    machine.Configuration.MatchId,
                    current.Revision,
                    machine.Configuration.BestOf,
                    manager,
                    eligible),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<TournamentMapSelectionResult> CompleteAsync(
        PlayerId manager,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var result = await _source.CompleteAsync(manager, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Tournament map selection did not produce a completed series.");

            var current = _runtime.CurrentSession
                ?? throw new InvalidOperationException("No active tournament match.");
            if (result.MatchId != current.Machine.Configuration.MatchId
                || result.Revision != current.Revision)
            {
                throw new TournamentConcurrencyException(
                    current.Machine.Configuration.MatchId,
                    result.Revision,
                    current.Revision);
            }

            var candidateMachine = TournamentMatchStateMachine.Restore(
                current.Machine.Configuration,
                current.Machine.Snapshot());
            candidateMachine.BeginVeto();
            candidateMachine.CompleteVeto(result.Maps);

            var candidate = new TournamentRecoverySession(
                candidateMachine, current.Revision);
            await _recovery.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);

            var stillCurrent = _runtime.CurrentSession;
            if (stillCurrent is null
                || stillCurrent.Machine.Configuration.MatchId != result.MatchId
                || stillCurrent.Revision != result.Revision)
            {
                throw new TournamentConcurrencyException(
                    result.MatchId,
                    result.Revision,
                    stillCurrent?.Revision ?? 0);
            }

            _runtime.Replace(candidate);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask CancelAsync(
        PlayerId manager,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await _source.CancelAsync(manager, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _gate.Dispose();
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
