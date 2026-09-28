using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Runtime.Settings;

public sealed class PlayerToggleCommandModule : IDisposable
{
    private const int PageSize = 3;
    private static readonly ModuleId Owner = new("core.settings");
    private readonly IPlayerRegistry _players;
    private readonly IPlayerToggleCatalog _catalog;
    private readonly IPlayerSettingsService _settings;
    private readonly IDisposable[] _commands;
    private int _disposed;

    public PlayerToggleCommandModule(
        IAnoCommandRegistry commands,
        IPlayerRegistry players,
        IPlayerToggleCatalog catalog,
        IPlayerSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        var registrations = new List<IDisposable>();
        try
        {
            registrations.Add(commands.Register(
                Owner,
                new CommandDescriptor("anosettings", "Show your player settings.",
                    arguments: [new("page", CommandArgumentKind.Int32,
                        "Page number.", required: false)]),
                ListAsync));
            registrations.Add(commands.Register(
                Owner,
                new CommandDescriptor("anotoggle", "Set or reset your player option.",
                    arguments:
                    [
                        new("key", CommandArgumentKind.String, "Setting key."),
                        new("value", CommandArgumentKind.String, "on, off or default."),
                    ]),
                SetAsync));
            _commands = registrations.ToArray();
        }
        catch
        {
            foreach (var registration in registrations)
                registration.Dispose();
            throw;
        }
    }

    private async ValueTask<CommandResult> ListAsync(CommandContext context)
    {
        if (!TryCurrent(context.Caller, out var player))
            return NoPlayer();
        var page = context.TryGet<int>("page", out var requested) ? requested : 1;
        if (page is < 1 or > 1000)
            return InvalidPage();
        var all = _catalog.GetAll();
        if (all.Count == 0 && page == 1)
            return CommandResult.Ok("No player settings available.");
        var start = (page - 1) * PageSize;
        if (start >= all.Count)
            return InvalidPage();
        var entries = new List<string>();
        foreach (var setting in all.Skip(start).Take(PageSize))
        {
            var value = await _settings.GetAsync(player!.Id, setting.Key,
                context.CancellationToken).ConfigureAwait(false);
            if (!TryCurrentSession(player))
                return NoPlayer();
            entries.Add($"{setting.Key.Name}={(value ? "on" : "off")}");
        }

        return CommandResult.Ok($"Settings {page}/{(all.Count + PageSize - 1) / PageSize}: "
            + string.Join(", ", entries));
    }

    private async ValueTask<CommandResult> SetAsync(CommandContext context)
    {
        if (!TryCurrent(context.Caller, out var player))
            return NoPlayer();
        var key = context.Get<string>("key");
        var action = context.Get<string>("value").ToLowerInvariant();
        if (!_catalog.TryGet(key, out var setting) || setting is null)
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "Unknown player setting.");
        if (action is not ("on" or "off" or "default"))
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "Use on, off or default.");
        if (!TryCurrentSession(player!))
            return NoPlayer();
        if (action == "default")
            await _settings.ResetAsync(player!.Id, setting.Key, context.CancellationToken)
                .ConfigureAwait(false);
        else
            await _settings.SetAsync(player!.Id, setting.Key, action == "on",
                context.CancellationToken).ConfigureAwait(false);
        return CommandResult.Ok($"[ANO] {setting.Key.Name} "
            + (action == "default" ? "reset to default." : $"set {action}."));
    }

    private bool TryCurrent(PlayerId? id, out PlayerSnapshot? player)
    {
        player = null;
        return Volatile.Read(ref _disposed) == 0 && id is not null
            && _players.TryGet(id, out player) && player?.IsConnected == true;
    }

    private bool TryCurrentSession(PlayerSnapshot player)
        => TryCurrent(player.Id, out var current)
            && current?.SessionId == player.SessionId;

    private static CommandResult NoPlayer()
        => CommandResult.Fail(CommandFailureReason.InvalidInput,
            "A connected player is required.");

    private static CommandResult InvalidPage()
        => CommandResult.Fail(CommandFailureReason.InvalidInput,
            "Page is outside the available settings.");

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        foreach (var command in _commands)
            command.Dispose();
    }
}
