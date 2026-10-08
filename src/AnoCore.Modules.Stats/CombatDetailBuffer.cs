using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

/// <summary>
/// Bounded volatile queue. Entries, including the in-flight prefix, remain until
/// the atomic database write succeeds. The host owns periodic start and shutdown.
/// </summary>
public sealed class CombatDetailBuffer
{
    private readonly ICombatDetailBatchRepository _repository;
    private readonly object _gate = new();
    private readonly Queue<Entry> _pending = new();
    private readonly SemaphoreSlim _flush = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly int _capacity;
    private readonly int _batchSize;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _shutdownTimeout;
    private Task? _worker;
    private long _sequence;
    private long _rejected;
    private bool _stopping;

    public CombatDetailBuffer(ICombatDetailBatchRepository repository, CombatRecordingConfiguration configuration)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        var errors = CombatRecordingConfiguration.Validate(configuration);
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors), nameof(configuration));
        _capacity = configuration.Capacity;
        _batchSize = configuration.BatchSize;
        _interval = TimeSpan.FromSeconds(configuration.FlushIntervalSeconds);
        _shutdownTimeout = TimeSpan.FromSeconds(configuration.ShutdownTimeoutSeconds);
    }

    public int PendingCount { get { lock (_gate) return _pending.Count; } }
    public long RejectedCount { get { lock (_gate) return _rejected; } }

    public bool TryEnqueue(CombatWeaponFireEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Enqueue(value, null);
    }

    public bool TryEnqueue(CombatDamageEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Enqueue(null, value);
    }

    private bool Enqueue(CombatWeaponFireEvent? shot, CombatDamageEvent? hit)
    {
        lock (_gate)
        {
            if (_stopping || _pending.Count >= _capacity)
            {
                _rejected++;
                return false;
            }
            _pending.Enqueue(new(++_sequence, shot, hit));
            return true;
        }
    }

    public void Start(Action<Exception> reportFailure)
    {
        ArgumentNullException.ThrowIfNull(reportFailure);
        lock (_gate)
        {
            if (_stopping) throw new InvalidOperationException("Combat buffer is stopping.");
            if (_worker is not null) throw new InvalidOperationException("Combat buffer is already running.");
            _worker = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(_interval);
                try
                {
                    while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                    {
                        try { await FlushAsync(_stop.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                        catch (Exception exception)
                        {
                            try { reportFailure(exception); }
                            catch { /* Reporting must not stop recovery of accepted events. */ }
                        }
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            });
        }
    }

    /// <summary>Flushes the accepted prefix captured at invocation; concurrent arrivals stay bounded.</summary>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long through;
        lock (_gate) through = _sequence;
        await _flush.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                Entry[] entries;
                lock (_gate) entries = _pending.TakeWhile(entry => entry.Sequence <= through).Take(_batchSize).ToArray();
                if (entries.Length == 0) return;
                var batch = new CombatDetailBatch(
                    entries.Where(entry => entry.Shot is not null).Select(entry => entry.Shot!),
                    entries.Where(entry => entry.Hit is not null).Select(entry => entry.Hit!));
                await _repository.RecordDetailsAsync(batch, cancellationToken).ConfigureAwait(false);
                lock (_gate)
                    for (var index = 0; index < entries.Length; index++) _pending.Dequeue();
            }
        }
        finally { _flush.Release(); }
    }

    public async ValueTask StopAsync()
    {
        Task? worker;
        lock (_gate)
        {
            _stopping = true;
            worker = _worker;
        }
        await _stop.CancelAsync().ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(_shutdownTimeout);
        if (worker is not null) await worker.WaitAsync(timeout.Token).ConfigureAwait(false);
        await FlushAsync(timeout.Token).ConfigureAwait(false);
    }

    private sealed record Entry(long Sequence, CombatWeaponFireEvent? Shot, CombatDamageEvent? Hit);
}
