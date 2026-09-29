using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;

namespace AnoCore.Runtime.Configuration;

public sealed class ConfigReloadCommandController : IDisposable
{
    private static readonly ModuleId CoreModule = new("core");
    private static readonly PermissionId ReloadPermission = new("ano.core.reload");
    private readonly IDisposable[] _registrations;
    private readonly IConfigReloadRegistry _reloads;
    private int _disposed;

    public ConfigReloadCommandController(
        IAnoCommandRegistry commands,
        IConfigReloadRegistry reloads)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _reloads = reloads ?? throw new ArgumentNullException(nameof(reloads));

        var registrations = new List<IDisposable>(2);
        try
        {
            registrations.Add(commands.Register(
                CoreModule,
                new CommandDescriptor(
                    "anoconfigs",
                    "List reloadable AnoCore configurations",
                    ReloadPermission),
                ListAsync));
            registrations.Add(commands.Register(
                CoreModule,
                new CommandDescriptor(
                    "anoreloadconfig",
                    "Reload one registered AnoCore configuration",
                    ReloadPermission,
                    arguments:
                    [
                        new CommandArgumentDescriptor(
                            "name",
                            CommandArgumentKind.String,
                            "Registered configuration name"),
                    ]),
                ReloadAsync));
            _registrations = registrations.ToArray();
        }
        catch
        {
            for (var index = registrations.Count - 1; index >= 0; index--)
                registrations[index].Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        for (var index = _registrations.Length - 1; index >= 0; index--)
            _registrations[index].Dispose();
    }

    private ValueTask<CommandResult> ListAsync(CommandContext context)
    {
        var configurations = _reloads.Configurations;
        var message = configurations.Count == 0
            ? "[ANO] No reloadable configurations are registered."
            : $"[ANO] Reloadable configurations: {string.Join(" | ", configurations.Select(value => $"{value.Name} ({value.Owner})"))}.";
        return ValueTask.FromResult(CommandResult.Ok(message));
    }

    private async ValueTask<CommandResult> ReloadAsync(CommandContext context)
    {
        var name = context.Get<string>("name");
        await _reloads.ReloadAsync(name, context.CancellationToken).ConfigureAwait(false);
        return CommandResult.Ok($"[ANO] Configuration '{name.Trim()}' reloaded.");
    }
}
