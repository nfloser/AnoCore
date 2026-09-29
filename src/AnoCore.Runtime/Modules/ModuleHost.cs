using System.Runtime.ExceptionServices;
using AnoCore.Abstractions.Modules;

namespace AnoCore.Runtime.Modules;

public sealed class ModuleHost
{
    private readonly object _sync = new();
    private readonly Dictionary<ModuleId, Registration> _registrations = [];
    private readonly IAnoModuleContext _context;

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

        var id = module.Descriptor.Id;
        Registration registration;

        lock (_sync)
        {
            if (_registrations.TryGetValue(id, out var existing)
                && existing.State is ModuleState.Loading or ModuleState.Loaded or ModuleState.Unloading)
            {
                throw new InvalidOperationException($"Module '{id}' is already active.");
            }

            registration = new Registration(
                module,
                new ModuleLifetimeContext(_context.Services),
                ModuleState.Loading);
            _registrations[id] = registration;
        }

        try
        {
            await module.InitializeAsync(registration.Context, cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
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

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
        return true;
    }

    private sealed class Registration(
        IAnoModule module,
        ModuleLifetimeContext context,
        ModuleState state)
    {
        public IAnoModule Module { get; } = module;

        public ModuleLifetimeContext Context { get; } = context;

        public ModuleState State { get; set; } = state;

        public Exception? Failure { get; set; }

        public ModuleSnapshot ToSnapshot() => new(Module.Descriptor, State, Failure);
    }
}
