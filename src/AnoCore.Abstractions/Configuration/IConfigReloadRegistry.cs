using AnoCore.Abstractions.Modules;

namespace AnoCore.Abstractions.Configuration;

public sealed record ConfigReloadDescriptor(string Name, ModuleId Owner);

public interface IConfigReloadRegistration<out T> : IDisposable
{
    ConfigReloadDescriptor Descriptor { get; }

    T Current { get; }
}

public interface IConfigReloadRegistry
{
    IReadOnlyCollection<ConfigReloadDescriptor> Configurations { get; }

    IConfigReloadRegistration<T> Register<T>(
        ModuleId owner,
        string name,
        T initialValue,
        Func<CancellationToken, ValueTask<T>> load,
        Func<T, IReadOnlyCollection<string>>? validate = null);

    ValueTask ReloadAsync(string name, CancellationToken cancellationToken = default);
}
