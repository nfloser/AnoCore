using AnoCore.Abstractions.Modules;

namespace AnoCore.Abstractions.Persistence;

public interface IModuleDataBatchStore
{
    ValueTask SetManyAsync(
        ModuleId module,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default);
}
