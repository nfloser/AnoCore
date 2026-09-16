using AnoCore.Abstractions.Modules;

namespace AnoCore.Abstractions.Commands;

public interface IAnoCommandRegistry
{
    void Register(ModuleId owner, CommandDescriptor descriptor);

    void UnregisterAll(ModuleId owner);
}
