using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

// Presentation only: never grants permissions or modifies CounterStrikeSharp admin data.
public sealed class RoleChatTagModule : IDisposable
{
    private readonly RoleChatTagPolicy _initial;
    private IConfigReloadRegistration<RoleChatTagPolicy>? _reload;
    private int _disposed;

    private RoleChatTagModule(RoleChatTagPolicy initial) => _initial = initial;

    public static async Task<RoleChatTagModule> CreateAsync(
        IConfigStore configuration, IConfigReloadRegistry? reloads = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        async ValueTask<RoleChatTagPolicy> Load(CancellationToken token)
            => RoleChatTagPolicy.Compile(await configuration.LoadAsync("role-chat-tags",
                () => RoleChatTagConfiguration.Default, RoleChatTagConfiguration.Validate, token)
                .ConfigureAwait(false));
        var module = new RoleChatTagModule(await Load(cancellationToken).ConfigureAwait(false));
        cancellationToken.ThrowIfCancellationRequested();
        module._reload = reloads?.Register(new ModuleId("ano.chat.roles"), "role-chat-tags", module._initial, Load);
        return module;
    }

    public string? Resolve(PlayerId player, IReadOnlyCollection<string> exactGroups, PlayerTeam team)
        => Volatile.Read(ref _disposed) == 0
            ? (_reload?.Current ?? _initial).Resolve(player, exactGroups, team) : null;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _reload?.Dispose();
    }
}
