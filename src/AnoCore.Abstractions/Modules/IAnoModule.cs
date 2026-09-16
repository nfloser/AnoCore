namespace AnoCore.Abstractions.Modules;

public interface IAnoModule
{
    ModuleDescriptor Descriptor { get; }

    /// <summary>
    /// Initializes the module. If this method throws, the runtime will make a best-effort
    /// call to <see cref="ShutdownAsync"/> so implementations must tolerate partial initialization.
    /// </summary>
    Task InitializeAsync(
        IAnoModuleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases module resources. Implementations must be safe to call after partial initialization.
    /// </summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
