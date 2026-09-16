using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Commands;

public interface IAnoCommandRegistry
{
    IDisposable Register(ModuleId owner, CommandDescriptor descriptor, AnoCommandHandler handler);

    void UnregisterAll(ModuleId owner);

    ValueTask<CommandResult> ExecuteAsync(
        string input,
        PlayerId? caller,
        CancellationToken cancellationToken = default);
}
