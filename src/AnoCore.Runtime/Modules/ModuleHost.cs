using System.Runtime.ExceptionServices;
using AnoCore.Abstractions.Modules;

namespace AnoCore.Runtime.Modules;

public sealed class ModuleHost
{
    private readonly object _sync = new();
    private readonly Dictionary<ModuleId, Registration> _registrations = [];
    private readonly IAnoModuleContext _context;
    private readonly CancellationTokenSource _lifetime = new();
    private TaskCompletionSource? _shutdown;
    private bool _stopping;
    private long _loadOrder;

    public ModuleHost(IAnoModuleContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IReadOnlyCollection<ModuleSnapshot> Modules
    {
        get
        {
            lock (_sync)
            {
                return _registrations.Values
                    .Select(registration => registration.ToSnapshot())
                    .ToArray();
            }
        }
    }

    public ModuleState GetState(ModuleId moduleId)
    {
        ArgumentNullException.ThrowIfNull(moduleId);

        lock (_sync)
        {
            if (!_registrations.TryGetValue(moduleId, out var registration))
            {
                throw new KeyNotFoundException($"Module '{moduleId}' is not registered.");
            }

            return registration.State;
        }
    }

    public async Task LoadAsync(IAnoModule module, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(module);

        var descriptor = module.Descriptor;
        if (descriptor.MinimumApiLevel < AnoCoreApi.MinimumSupportedLevel
            || descriptor.MinimumApiLevel > AnoCoreApi.CurrentLevel)
        {
            throw new NotSupportedException(
                $"Module '{descriptor.Id}' requires AnoCore API level {descriptor.MinimumApiLevel}; "
                + $"this runtime supports levels {AnoCoreApi.MinimumSupportedLevel}-{AnoCoreApi.CurrentLevel}.");
        }

        var id = descriptor.Id;
        Registration registration;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_registrations.TryGetValue(id, out var existing)
                && existing.State is ModuleState.Loading or ModuleState.Loaded or ModuleState.Unloading)
            {
                throw new InvalidOperationException($"Module '{id}' is already active.");
            }

            registration = new Registration(
                module,
                new ModuleLifetimeContext(_context.Services),
                ModuleState.Loading,
                ++_loadOrder);
            _registrations[id] = registration;
        }

        var loadCompletion = registration.Completion;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            linked.Token.ThrowIfCancellationRequested();
            await module.InitializeAsync(registration.Context, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();

            lock (_sync)
            {
                if (_stopping) throw new OperationCanceledException("AnoCore module host is stopping.");
                registration.State = ModuleState.Loaded;
                registration.Failure = null;
            }
        }
        catch (Exception initializationException)
        {
            var failures = new List<Exception> { initializationException };

            try
            {
                await module.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollbackException)
            {
                failures.Add(rollbackException);
            }

            try
            {
                registration.Context.Dispose();
            }
            catch (Exception cleanupException)
            {
                failures.Add(cleanupException);
            }

            Exception recordedFailure = failures.Count == 1
                ? initializationException
                : new AggregateException(
                    "Module initialization failed and rollback cleanup was incomplete.", failures);

            lock (_sync)
            {
                registration.State = ModuleState.Faulted;
                registration.Failure = recordedFailure;
            }

            throw;
        }
        finally { loadCompletion.TrySetResult(); }
    }

    public async Task<bool> UnloadAsync(ModuleId moduleId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(moduleId);

        Registration registration;

        lock (_sync)
        {
            if (!_registrations.TryGetValue(moduleId, out registration!))
            {
                return false;
            }

            if (registration.State != ModuleState.Loaded)
            {
                return false;
            }

            registration.State = ModuleState.Unloading;
            registration.Completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        Exception? failure = null;
        try
        {
            await registration.Module.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            registration.Context.Dispose();
        }
        catch (Exception cleanupException)
        {
            failure = failure is null
                ? cleanupException
                : new AggregateException(
                    "Module shutdown and owned-resource cleanup both failed.",
                    failure,
                    cleanupException);
        }

        lock (_sync)
        {
            registration.State = failure is null ? ModuleState.Unloaded : ModuleState.Faulted;
            registration.Failure = failure;
        }
        registration.Completion.TrySetResult();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
        return true;
    }

    // Start teardown synchronously: release registrations before any asynchronous shutdown can yield.
    public Task ShutdownAsync()
    {
        Registration[] registrations;
        TaskCompletionSource completion;
        lock (_sync)
        {
            if (_shutdown is not null) return _shutdown.Task;
            _stopping = true;
            completion = _shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            registrations = _registrations.Values.OrderByDescending(item => item.Order).ToArray();
        }
        var failures = new List<Exception>();
        try { _lifetime.Cancel(); }
        catch (Exception exception) { failures.Add(exception); }
        foreach (var registration in registrations)
        {
            try { registration.Context.Dispose(); }
            catch (Exception exception) { failures.Add(exception); }
        }
        _ = CompleteShutdownAsync(registrations, failures, completion);
        return completion.Task;
    }

    private async Task CompleteShutdownAsync(Registration[] registrations, List<Exception> failures,
        TaskCompletionSource completion)
    {
        foreach (var registration in registrations)
        {
            try
            {
                while (true)
                {
                    ModuleState state;
                    Task operation;
                    lock (_sync) { state = registration.State; operation = registration.Completion.Task; }
                    if (state is ModuleState.Loading or ModuleState.Unloading)
                    {
                        await operation.ConfigureAwait(false);
                        continue;
                    }
                    if (state == ModuleState.Loaded)
                        await UnloadAsync(registration.Module.Descriptor.Id).ConfigureAwait(false);
                    else if (state == ModuleState.Faulted && registration.Failure is { } failure
                        && failure is not OperationCanceledException)
                        ExceptionDispatchInfo.Capture(failure).Throw();
                    break;
                }
            }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count == 0) completion.TrySetResult();
        else completion.TrySetException(new AggregateException("Module host shutdown was incomplete.", failures));
    }

    private sealed class Registration(
        IAnoModule module,
        ModuleLifetimeContext context,
        ModuleState state,
        long order)
    {
        public IAnoModule Module { get; } = module;

        public ModuleLifetimeContext Context { get; } = context;

        public ModuleState State { get; set; } = state;

        public Exception? Failure { get; set; }
        public long Order { get; } = order;
        public TaskCompletionSource Completion { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ModuleSnapshot ToSnapshot() => new(Module.Descriptor, State, Failure);
    }
}
