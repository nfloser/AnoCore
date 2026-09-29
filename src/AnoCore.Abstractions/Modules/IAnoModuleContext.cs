namespace AnoCore.Abstractions.Modules;

public interface IAnoModuleContext
{
    IServiceProvider Services { get; }

    /// <summary>
    /// Transfers a disposable registration to the current module load scope.
    /// The runtime disposes owned resources in reverse order after shutdown or rollback.
    /// Custom contexts that do not provide ownership retain the resource unchanged.
    /// </summary>
    T Own<T>(T resource)
        where T : IDisposable
        => resource ?? throw new ArgumentNullException(nameof(resource));
}
