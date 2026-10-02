using AnoCore.Abstractions.Modules;

namespace AnoCore.Abstractions.Placeholders;

public delegate ValueTask<string?> PlaceholderResolver(
    PlaceholderContext context,
    CancellationToken cancellationToken);

public interface IPlaceholderRegistry
{
    IDisposable Register(ModuleId owner, string name, PlaceholderResolver resolver);

    IDisposable RegisterPrioritized(
        ModuleId owner,
        string name,
        int priority,
        PlaceholderResolver resolver);

    bool Contains(string name);

    int RemoveOwner(ModuleId owner);

    ValueTask<string> ResolveAsync(
        string template,
        PlaceholderContext context,
        CancellationToken cancellationToken = default);
}
