using System.Runtime.ExceptionServices;
using AnoCore.Abstractions.Modules;

namespace AnoCore.Runtime.Modules;

internal sealed class ModuleLifetimeContext : IAnoModuleContext, IDisposable
{
    private readonly object _gate = new();
    private List<IDisposable>? _resources = [];

    public ModuleLifetimeContext(IServiceProvider services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceProvider Services { get; }

    public T Own<T>(T resource)
        where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
        {
            if (_resources is null)
            {
                resource.Dispose();
                throw new ObjectDisposedException(nameof(ModuleLifetimeContext));
            }

            _resources.Add(resource);
            return resource;
        }
    }

    public void Dispose()
    {
        List<IDisposable>? resources;
        lock (_gate)
        {
            resources = _resources;
            _resources = null;
        }

        if (resources is null)
            return;

        List<Exception>? failures = null;
        for (var index = resources.Count - 1; index >= 0; index--)
        {
            try
            {
                resources[index].Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is [var single])
            ExceptionDispatchInfo.Capture(single).Throw();
        if (failures is not null)
            throw new AggregateException("Multiple module-owned resources failed to dispose.", failures);
    }
}
