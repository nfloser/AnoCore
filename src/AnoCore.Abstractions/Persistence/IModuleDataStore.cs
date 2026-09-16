using AnoCore.Abstractions.Modules;

namespace AnoCore.Abstractions.Persistence;

public interface IModuleDataStore
{
    ValueTask<string?> GetAsync(
        ModuleId module,
        string key,
        CancellationToken cancellationToken = default);

    ValueTask SetAsync(
        ModuleId module,
        string key,
        string json,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(
        ModuleId module,
        string key,
        CancellationToken cancellationToken = default);
}
