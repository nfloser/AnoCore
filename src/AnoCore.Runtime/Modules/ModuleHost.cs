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

            registration = new Registration(module, ModuleState.Loading);
            _registrations[id] = registration;
        }

        try
        {
            await module.InitializeAsync(_context, cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
                registration.State = ModuleState.Loaded;
                registration.Failure = null;
            }
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                registration.State = ModuleState.Faulted;
                registration.Failure = exception;
            }

            throw;
        }
    }

    public async Task<bool> UnloadAsync(ModuleId moduleId, CancellationToken cancellationToken = default)
    {
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

        try
        {
            await registration.Module.ShutdownAsync(cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
                registration.State = ModuleState.Unloaded;
                registration.Failure = null;
            }

            return true;
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                registration.State = ModuleState.Faulted;
                registration.Failure = exception;
            }

            throw;
        }
    }

    private sealed class Registration(IAnoModule module, ModuleState state)
    {
        public IAnoModule Module { get; } = module;

        public ModuleState State { get; set; } = state;

        public Exception? Failure { get; set; }

        public ModuleSnapshot ToSnapshot() => new(Module.Descriptor, State, Failure);
    }
}
