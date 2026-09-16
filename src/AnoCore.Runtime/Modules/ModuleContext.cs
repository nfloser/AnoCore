using AnoCore.Abstractions.Modules;

namespace AnoCore.Runtime.Modules;

public sealed class ModuleContext(IServiceProvider services) : IAnoModuleContext
{
    public IServiceProvider Services { get; } = services ?? throw new ArgumentNullException(nameof(services));
}
