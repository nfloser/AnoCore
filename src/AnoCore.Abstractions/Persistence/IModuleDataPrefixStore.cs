using AnoCore.Abstractions.Modules;

namespace AnoCore.Abstractions.Persistence;

public interface IModuleDataPrefixStore
{
    ValueTask<int> DeleteByPrefixAsync(
        ModuleId module,
        string keyPrefix,
        CancellationToken cancellationToken = default);
}
