using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;

namespace AnoCore.TestModuleFixture;

public sealed class FixtureModule : IAnoModule
{
    public ModuleDescriptor Descriptor { get; } = new(new ModuleId("example.fixture"),
        "SDK fixture", "1.0.0", "External SDK-only integration fixture", AnoCoreApi.CurrentLevel);

    public Task InitializeAsync(IAnoModuleContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var commands = (IAnoCommandRegistry)context.Services.GetService(typeof(IAnoCommandRegistry))!;
        context.Own(commands.Register(Descriptor.Id, new CommandDescriptor("anofixture", "SDK fixture command"),
            _ => ValueTask.FromResult(CommandResult.Ok("External SDK module ready."))));
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
