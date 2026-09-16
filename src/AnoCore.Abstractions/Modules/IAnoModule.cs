namespace AnoCore.Abstractions.Modules;

public interface IAnoModule
{
    ModuleDescriptor Descriptor { get; }

    Task InitializeAsync(
        IAnoModuleContext context,
        CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
