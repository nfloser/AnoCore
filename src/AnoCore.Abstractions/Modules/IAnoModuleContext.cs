namespace AnoCore.Abstractions.Modules;

public interface IAnoModuleContext
{
    IServiceProvider Services { get; }
}
