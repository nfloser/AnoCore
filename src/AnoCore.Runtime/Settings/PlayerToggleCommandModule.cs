using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Runtime.Settings;

public sealed class PlayerToggleCommandModule : IDisposable
{
    private const int PageSize = 3;
    public const string MenuCommandName = "anosettingsmenu";
    private static readonly ModuleId Owner = new("core.settings");
    private readonly object _menuGate = new();
    private readonly Dictionary<PlayerId, MenuRegistration> _playerMenus = [];
    private readonly IMenuService? _menus;
    private readonly IDisposable[] _subscriptions;
    private int _generation;
    private readonly IPlayerRegistry _players;
    private readonly IPlayerToggleCatalog _catalog;
    private readonly IPlayerSettingsService _settings;
    private readonly IDisposable[] _commands;
    private int _disposed;

    public PlayerToggleCommandModule(
        IAnoCommandRegistry commands,
        IPlayerRegistry players,
        IPlayerToggleCatalog catalog,
        IPlayerSettingsService settings,
        IMenuService? menus = null,
        IAnoEventBus? events = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _menus = menus;
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        var registrations = new List<IDisposable>();
        var subscriptions = new List<IDisposable>();
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
            if (menus is not null)
            {
                registrations.Add(commands.Register(
                    Owner,
                    new CommandDescriptor(MenuCommandName,
                        "Open your player settings menu.",
                        arguments: [new("page", CommandArgumentKind.Int32,
                            "Page number.", required: false)]),
                    OpenMenuAsync));
            }
            if (events is not null)
            {
                subscriptions.Add(events.Subscribe<PlayerDisconnectedEvent>(
                    (value, _) =>
                    {
                        RemoveSessionMenu(value.Player);
                        return ValueTask.CompletedTask;
                    }));
                subscriptions.Add(events.Subscribe<PlayerReconnectedEvent>(
                    (value, _) =>
                    {
                        RemoveSessionMenu(value.Previous);
                        return ValueTask.CompletedTask;
                    }));
            }
            _commands = registrations.ToArray();
            _subscriptions = subscriptions.ToArray();
        }
        catch
        {
            foreach (var subscription in subscriptions)
                subscription.Dispose();
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

    private async ValueTask<CommandResult> OpenMenuAsync(CommandContext context)
    {
        if (_menus is null || !TryCurrent(context.Caller, out var player))
            return NoPlayer();
        var page = context.TryGet<int>("page", out var requested) ? requested : 1;
        if (!await OpenPageAsync(player!, page, context.CancellationToken)
            .ConfigureAwait(false))
            return InvalidPage();
        return CommandResult.Ok("[ANO] Settings menu opened.");
    }

    private async ValueTask<bool> OpenPageAsync(
        PlayerSnapshot player, int page, CancellationToken cancellationToken)
    {
        if (_menus is null || page is < 1 or > 1000 || !TryCurrentSession(player))
            return false;
        var all = _catalog.GetAll();
        var start = (page - 1) * PageSize;
        if (start >= all.Count)
            return false;
        var generation = Interlocked.Increment(ref _generation).ToString("x8");
        var options = new List<MenuOption>();
        foreach (var setting in all.Skip(start).Take(PageSize))
        {
            var current = await _settings.GetAsync(
                player.Id, setting.Key, cancellationToken).ConfigureAwait(false);
            if (!TryCurrentSession(player))
                return false;
            var selected = setting;
            var nextValue = !current;
            var index = options.Count;
            options.Add(new MenuOption(
                $"t{generation}_{index}", $"{selected.Label}: {(current ? "on" : "off")}",
                async context =>
                {
                    if (!StillAvailable(player, selected))
                        return;
                    await _settings.SetAsync(player.Id, selected.Key, nextValue,
                        context.CancellationToken).ConfigureAwait(false);
                    await OpenPageAsync(player, page, context.CancellationToken)
                        .ConfigureAwait(false);
                }, keepOpen: true));
            options.Add(new MenuOption(
                $"r{generation}_{index}", $"Default: {selected.Label}",
                async context =>
                {
                    if (!StillAvailable(player, selected))
                        return;
                    await _settings.ResetAsync(player.Id, selected.Key,
                        context.CancellationToken).ConfigureAwait(false);
                    await OpenPageAsync(player, page, context.CancellationToken)
                        .ConfigureAwait(false);
                }, keepOpen: true));
        }

        if (page > 1)
            options.Add(new MenuOption(
                $"p{generation}", "Previous page",
                async context =>
                {
                    await OpenPageAsync(
                        player, page - 1, context.CancellationToken).ConfigureAwait(false);
                },
                keepOpen: true));
        if (all.Count > start + PageSize && page < 1000)
            options.Add(new MenuOption(
                $"n{generation}", "Next page",
                async context =>
                {
                    await OpenPageAsync(
                        player, page + 1, context.CancellationToken).ConfigureAwait(false);
                },
                keepOpen: true));
        var definition = new MenuDefinition(
            new MenuId($"ano.settings.{player.Id.SteamId64}"),
            $"Settings — page {page}", options);
        lock (_menuGate)
        {
            if (!TryCurrentSession(player))
                return false;
            if (_playerMenus.Remove(player.Id, out var previous))
                previous.Handle.Dispose();
            var handle = _menus.Register(Owner, definition);
            _playerMenus[player.Id] = new MenuRegistration(player.SessionId, handle);
            _menus.Open(player.Id, definition.Id);
            return true;
        }
    }

    private bool StillAvailable(PlayerSnapshot player, PlayerToggleSetting setting)
        => TryCurrentSession(player)
            && _catalog.TryGet(setting.Key.Name, out var current)
            && ReferenceEquals(current, setting);

    private void RemoveSessionMenu(PlayerSnapshot player)
    {
        lock (_menuGate)
        {
            if (_playerMenus.TryGetValue(player.Id, out var registration)
                && registration.SessionId == player.SessionId)
            {
                _playerMenus.Remove(player.Id);
                registration.Handle.Dispose();
            }
        }
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
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        lock (_menuGate)
        {
            foreach (var registration in _playerMenus.Values)
                registration.Handle.Dispose();
            _playerMenus.Clear();
        }
        foreach (var command in _commands)
            command.Dispose();
    }

    private sealed record MenuRegistration(PlayerSessionId SessionId, IDisposable Handle);
}
