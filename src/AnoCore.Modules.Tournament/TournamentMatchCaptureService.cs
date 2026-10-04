namespace AnoCore.Modules.Tournament;

public sealed class TournamentMatchCaptureService : IAsyncDisposable, IDisposable
{
    private readonly TournamentMatchRuntime _runtime;
    private readonly ITournamentMatchCaptureTransport _transport;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly List<TournamentRoundBackup> _backups = [];
    private readonly int _maxRetainedBackups;
    private readonly Action<Exception>? _cleanupError;
    private TournamentDemoSession? _demo;
    private int _disposed;

    public TournamentMatchCaptureService(
        TournamentMatchRuntime runtime,
        ITournamentMatchCaptureTransport transport,
        int maxRetainedBackups = 32,
        Action<Exception>? cleanupError = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        if (maxRetainedBackups is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(maxRetainedBackups));
        _maxRetainedBackups = maxRetainedBackups;
        _cleanupError = cleanupError;
    }

    public TournamentDemoSession? ActiveDemo => Volatile.Read(ref _demo);

    public IReadOnlyList<TournamentRoundBackup> Backups
    {
        get
        {
            lock (_backups)
            {
                return _backups
                    .OrderByDescending(value => value.Revision)
                    .ThenByDescending(value => value.RoundNumber)
                    .ToArray();
            }
        }
    }

    public async ValueTask<TournamentDemoSession> StartDemoAsync(
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var context = RequireMapContext(expectedRevision, requireLive: false);
            var current = _demo;
            if (current is not null
                && current.MatchId == context.MatchId
                && current.MapIndex == context.MapIndex)
            {
                return current;
            }

            if (current is not null)
            {
                await _transport.StopDemoAsync(current, cancellationToken).ConfigureAwait(false);
                _demo = null;
            }

            var demo = new TournamentDemoSession(
                context.MatchId, context.MapIndex, context.MapName, context.Revision);
            await _transport.StartDemoAsync(demo, cancellationToken).ConfigureAwait(false);
            _demo = demo;
            return demo;
        }
        finally
        {
            _serial.Release();
        }
    }

    public async ValueTask<bool> StopDemoAsync(
        Guid matchId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (matchId == Guid.Empty)
            throw new ArgumentException("A match id is required.", nameof(matchId));

        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var current = _demo;
            if (current is null || current.MatchId != matchId)
                return false;

            await _transport.StopDemoAsync(current, cancellationToken).ConfigureAwait(false);
            _demo = null;
            return true;
        }
        finally
        {
            _serial.Release();
        }
    }

    public async ValueTask<TournamentRoundBackup> CaptureRoundAsync(
        int roundNumber,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (roundNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(roundNumber));

        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var context = RequireMapContext(expectedRevision, requireLive: true);
            var backup = new TournamentRoundBackup(
                context.MatchId,
                context.MapIndex,
                context.MapName,
                roundNumber,
                context.Revision);

            lock (_backups)
            {
                var existing = _backups.FirstOrDefault(value =>
                    string.Equals(value.Id, backup.Id, StringComparison.Ordinal));
                if (existing is not null)
                    return existing;
            }

            await _transport.CaptureBackupAsync(backup, cancellationToken).ConfigureAwait(false);

            lock (_backups)
            {
                _backups.Add(backup);
                while (_backups.Count > _maxRetainedBackups)
                    _backups.RemoveAt(0);
            }

            return backup;
        }
        finally
        {
            _serial.Release();
        }
    }

    public async ValueTask<TournamentRoundBackup> RestoreAsync(
        string backupId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(backupId))
            throw new ArgumentException("A backup id is required.", nameof(backupId));

        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var context = RequireMapContext(expectedRevision, requireLive: true);
            TournamentRoundBackup backup;
            lock (_backups)
            {
                backup = _backups.FirstOrDefault(value =>
                    string.Equals(value.Id, backupId.Trim(), StringComparison.Ordinal))
                    ?? throw new KeyNotFoundException("Tournament round backup was not found.");
            }

            if (backup.MatchId != context.MatchId)
                throw new InvalidOperationException("The backup belongs to another tournament match.");
            if (backup.MapIndex != context.MapIndex
                || !string.Equals(backup.MapName, context.MapName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The backup belongs to another tournament map.");
            }
            if (backup.Revision > context.Revision)
                throw new InvalidOperationException("The backup belongs to a newer tournament revision.");

            await _transport.RestoreBackupAsync(backup, cancellationToken).ConfigureAwait(false);
            return backup;
        }
        finally
        {
            _serial.Release();
        }
    }

    public async ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0 && _demo is null)
            return;

        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = _demo;
            if (current is null)
                return;

            try
            {
                await _transport.StopDemoAsync(current, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                _cleanupError?.Invoke(exception);
            }
            finally
            {
                _demo = null;
            }
        }
        finally
        {
            _serial.Release();
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private CaptureContext RequireMapContext(long expectedRevision, bool requireLive)
    {
        if (expectedRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));

        var session = _runtime.CurrentSession
            ?? throw new InvalidOperationException("No tournament match is active.");
        if (session.Revision != expectedRevision)
            throw new TournamentConcurrencyException(
                session.Machine.Configuration.MatchId, expectedRevision, session.Revision);

        var machine = session.Machine;
        if (machine.State == TournamentMatchState.Completed)
            throw new InvalidOperationException("The tournament match is already completed.");
        if (requireLive
            && machine.State is not (
                TournamentMatchState.Live
                or TournamentMatchState.Paused
                or TournamentMatchState.Overtime))
        {
            throw new InvalidOperationException(
                $"Round capture/restore is not available while the match is {machine.State}.");
        }

        if (machine.Maps.Count == 0
            || machine.CurrentMapIndex < 0
            || machine.CurrentMapIndex >= machine.Maps.Count)
        {
            throw new InvalidOperationException("The active tournament match has no current map.");
        }

        return new CaptureContext(
            machine.Configuration.MatchId,
            session.Revision,
            machine.CurrentMapIndex,
            machine.Maps[machine.CurrentMapIndex]);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _ = CleanupOwnedDemoAsync(CancellationToken.None);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        await CleanupOwnedDemoAsync(CancellationToken.None).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task CleanupOwnedDemoAsync(CancellationToken cancellationToken)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = _demo;
            if (current is null)
                return;

            try
            {
                await _transport.StopDemoAsync(current, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                _cleanupError?.Invoke(exception);
            }
            finally
            {
                _demo = null;
            }
        }
        finally
        {
            _serial.Release();
        }
    }

    private sealed record CaptureContext(
        Guid MatchId,
        long Revision,
        int MapIndex,
        string MapName);
}
