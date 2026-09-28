using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Placeholders;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Modules.Admin;

public sealed class SelectableChatTagModule : IDisposable
{
    public const string MenuCommandName = "anochatmenu";
    private const int PageSize = 5;
    private static readonly ModuleId Owner = new("ano.chat.tags");
    private static readonly PlayerSettingKey<string> SelectedTag = new("chat.tag.selected", "");
    private readonly object _menuGate = new();
    private readonly IPlayerRegistry _players;
    private readonly IMenuService? _menus;
    private readonly Dictionary<PlayerId, IDisposable> _playerMenus = [];
    private readonly IPlayerSettingsService _settings;
    private readonly IPermissionEvaluator _permissions;
    private readonly IAuthorizationReloadEvents _reloadEvents;
    private readonly Func<PlayerSnapshot, CancellationToken, ValueTask> _refresh;
    private readonly Action<Exception>? _onFailure;
    private readonly IReadOnlyDictionary<string, TagOption> _tags;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly IDisposable _provider;
    private readonly IDisposable[] _commands;
    private int _disposed;

    private SelectableChatTagModule(
        SelectableChatTagConfiguration configuration,
        IAnoCommandRegistry commands,
        IPlaceholderRegistry placeholders,
        IPlayerRegistry players,
        IPlayerSettingsService settings,
        IPermissionEvaluator permissions,
        IAuthorizationReloadEvents reloadEvents,
        Func<PlayerSnapshot, CancellationToken, ValueTask> refresh,
        IMenuService? menus,
        Action<Exception>? onFailure)
    {
        _lifetimeToken = _lifetime.Token;
        _players = players;
        _menus = menus;
        _settings = settings;
        _permissions = permissions;
        _reloadEvents = reloadEvents;
        _refresh = refresh;
        _onFailure = onFailure;
        _tags = configuration.Tags.ToDictionary(
            tag => tag.Id,
            tag => new TagOption(tag.Text, new PermissionId(tag.Permission)),
            StringComparer.OrdinalIgnoreCase);
        _provider = placeholders.RegisterPrioritized(
            Owner, "chat.tag", 100, ResolveTagAsync);
        var registrations = new List<IDisposable>();
        try
        {
            registrations.Add(commands.Register(
                Owner, new CommandDescriptor("anotags", "List available chat tags."),
                ListAsync));
            registrations.Add(commands.Register(
                Owner, new CommandDescriptor(
                    "anosettag", "Choose an available chat tag.",
                    arguments: [new("tag", CommandArgumentKind.String, "Chat tag ID.")]),
                SetAsync));
            registrations.Add(commands.Register(
                Owner, new CommandDescriptor(
                    "anocleartag", "Return to your default chat tag."),
                ClearAsync));
            if (menus is not null)
            {
                registrations.Add(commands.Register(
                    Owner, new CommandDescriptor(
                        MenuCommandName, "Open your chat tag selection menu.",
                        arguments: [new("page", CommandArgumentKind.Int32,
                            "Page number.", required: false)]),
                    OpenMenuAsync));
            }

            _commands = registrations.ToArray();
            _reloadEvents.Reloaded += OnReloaded;
        }
        catch
        {
            foreach (var registration in registrations)
                registration.Dispose();
            _provider.Dispose();
            _lifetime.Dispose();
            throw;
        }
    }

    public static async Task<SelectableChatTagModule> CreateAsync(
        IConfigStore configuration,
        IAnoCommandRegistry commands,
        IPlaceholderRegistry placeholders,
        IPlayerRegistry players,
        IPlayerSettingsService settings,
        IPermissionEvaluator permissions,
        IAuthorizationReloadEvents reloadEvents,
        Func<PlayerSnapshot, CancellationToken, ValueTask> refresh,
        IMenuService? menus = null,
        Action<Exception>? onFailure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(placeholders);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(reloadEvents);
        ArgumentNullException.ThrowIfNull(refresh);
        var loaded = await configuration.LoadAsync(
            "chat-tags",
            () => SelectableChatTagConfiguration.Default,
            SelectableChatTagConfiguration.Validate,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new SelectableChatTagModule(loaded, commands, placeholders,
            players, settings, permissions, reloadEvents, refresh, menus, onFailure);
    }

    private async ValueTask<string?> ResolveTagAsync(
        PlaceholderContext context,
        CancellationToken cancellationToken)
    {
        var player = context.Values.FirstOrDefault(pair =>
            string.Equals(pair.Key, "player", StringComparison.OrdinalIgnoreCase)).Value;
        if (player is not PlayerId playerId)
            return null;
        var selected = await _settings.GetAsync(playerId, SelectedTag, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(selected)
            || !_tags.TryGetValue(selected, out var option))
            return null;
        return await _permissions.HasPermissionAsync(
            playerId, option.Permission, cancellationToken).ConfigureAwait(false)
            ? option.Text
            : null;
    }

    private async ValueTask<CommandResult> ListAsync(CommandContext context)
    {
        if (!TryCurrent(context.Caller, out var player))
            return NoPlayer();
        var available = new List<string>();
        foreach (var tag in _tags.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (await _permissions.HasPermissionAsync(
                player!.Id, tag.Value.Permission, context.CancellationToken)
                .ConfigureAwait(false))
                available.Add(tag.Key);
        }

        return CommandResult.Ok(
            available.Count == 0 ? "No chat tags available."
                : $"Available chat tags: {string.Join(", ", available)}.");
    }

    private async ValueTask<CommandResult> SetAsync(CommandContext context)
    {
        if (!TryCurrent(context.Caller, out var player))
            return NoPlayer();
        var selected = context.Get<string>("tag");
        if (!_tags.TryGetValue(selected, out var option))
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "Unknown chat tag.");
        if (!await _permissions.HasPermissionAsync(
            player!.Id, option.Permission, context.CancellationToken)
            .ConfigureAwait(false))
            return CommandResult.Fail(CommandFailureReason.Forbidden,
                "Chat tag is unavailable.");
        if (!TryCurrentSession(player))
            return NoPlayer();
        return await TrySetChoiceAsync(player, selected, context.CancellationToken)
            .ConfigureAwait(false)
            ? CommandResult.Ok("[ANO] Chat tag selected.")
            : NoPlayer();
    }

    private async ValueTask<CommandResult> ClearAsync(CommandContext context)
    {
        if (!TryCurrent(context.Caller, out var player))
            return NoPlayer();
        return await TryClearChoiceAsync(player!, context.CancellationToken)
            .ConfigureAwait(false)
            ? CommandResult.Ok("[ANO] Chat tag cleared.")
            : NoPlayer();
    }

    private async ValueTask<CommandResult> OpenMenuAsync(CommandContext context)
    {
        if (_menus is null || !TryCurrent(context.Caller, out var player))
            return NoPlayer();
        var page = context.ParsedArguments.TryGetValue("page", out var raw)
            ? (int)raw! : 1;
        if (page is < 1 or > 1000)
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "Page must be between 1 and 1000.");
        await OpenMenuPageAsync(player!, page, context.CancellationToken)
            .ConfigureAwait(false);
        return CommandResult.Ok("[ANO] Chat tag menu opened.");
    }

    private async ValueTask OpenMenuPageAsync(
        PlayerSnapshot player, int page, CancellationToken cancellationToken)
    {
        if (_menus is null || !TryCurrentSession(player))
            return;
        var available = new List<KeyValuePair<string, TagOption>>();
        foreach (var tag in _tags.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _permissions.HasPermissionAsync(
                player.Id, tag.Value.Permission, cancellationToken)
                .ConfigureAwait(false))
                available.Add(tag);
        }

        var selected = await _settings.GetAsync(
            player.Id, SelectedTag, cancellationToken).ConfigureAwait(false);
        var currentLabel = "Current: rank tag";
        if (!string.IsNullOrEmpty(selected)
            && _tags.TryGetValue(selected, out var current)
            && await _permissions.HasPermissionAsync(
                player.Id, current.Permission, cancellationToken).ConfigureAwait(false))
            currentLabel = $"Current: {current.Text}";
        var start = (page - 1) * PageSize;
        var options = new List<MenuOption>
        {
            new("current", currentLabel, _ => ValueTask.CompletedTask, keepOpen: true),
        };
        foreach (var tag in available.Skip(start).Take(PageSize))
        {
            var selectedTag = tag;
            options.Add(new MenuOption(
                $"tag{options.Count - 1}", $"{selectedTag.Value.Text} ({selectedTag.Key})",
                async context =>
                {
                    if (await TrySetChoiceAsync(
                        player, selectedTag.Key, context.CancellationToken)
                        .ConfigureAwait(false))
                    {
                        await OpenMenuPageAsync(player, page, context.CancellationToken)
                            .ConfigureAwait(false);
                    }
                }, keepOpen: true));
        }

        options.Add(new MenuOption(
            "clear", "Use rank tag",
            async context =>
            {
                if (await TryClearChoiceAsync(player, context.CancellationToken)
                    .ConfigureAwait(false))
                {
                    await OpenMenuPageAsync(player, page, context.CancellationToken)
                        .ConfigureAwait(false);
                }
            }, keepOpen: true));
        if (page > 1)
            options.Add(new MenuOption(
                "previous", "Previous page",
                context => OpenMenuPageAsync(player, page - 1, context.CancellationToken),
                keepOpen: true));
        if (available.Count > start + PageSize && page < 1000)
            options.Add(new MenuOption(
                "next", "Next page",
                context => OpenMenuPageAsync(player, page + 1, context.CancellationToken),
                keepOpen: true));
        if (!TryCurrentSession(player))
            return;
        var definition = new MenuDefinition(
            new MenuId($"ano.chat.tags.{player.Id.SteamId64}"),
            $"Chat tags — page {page}", options);
        lock (_menuGate)
        {
            if (!TryCurrentSession(player))
                return;
            if (_playerMenus.Remove(player.Id, out var previous))
                previous.Dispose();
            var registration = _menus.Register(Owner, definition);
            _playerMenus[player.Id] = registration;
            _menus.Open(player.Id, definition.Id);
        }
    }

    private async ValueTask<bool> TrySetChoiceAsync(
        PlayerSnapshot player, string selected, CancellationToken cancellationToken)
    {
        if (!_tags.TryGetValue(selected, out var option)
            || !await _permissions.HasPermissionAsync(
                player.Id, option.Permission, cancellationToken).ConfigureAwait(false)
            || !TryCurrentSession(player))
            return false;
        await _settings.SetAsync(player.Id, SelectedTag, selected.ToLowerInvariant(),
            cancellationToken).ConfigureAwait(false);
        await RefreshCurrentAsync(player, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> TryClearChoiceAsync(
        PlayerSnapshot player, CancellationToken cancellationToken)
    {
        if (!TryCurrentSession(player))
            return false;
        await _settings.ResetAsync(player.Id, SelectedTag, cancellationToken)
            .ConfigureAwait(false);
        await RefreshCurrentAsync(player, cancellationToken).ConfigureAwait(false);
        return true;
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

    private async ValueTask RefreshCurrentAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken)
    {
        if (!TryCurrentSession(player))
            return;
        try
        {
            await _refresh(player, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _onFailure?.Invoke(exception);
        }
    }

    private void OnReloaded()
    {
        if (Volatile.Read(ref _disposed) == 0)
            _ = RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        var work = new List<Task>();
        foreach (var player in _players.OnlinePlayers.ToArray())
        {
            if (Volatile.Read(ref _disposed) != 0)
                break;
            work.Add(RefreshOneAsync(player));
        }

        await Task.WhenAll(work).ConfigureAwait(false);
    }

    private async Task RefreshOneAsync(PlayerSnapshot player)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeToken);
            await RefreshCurrentAsync(player, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _onFailure?.Invoke(exception);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _reloadEvents.Reloaded -= OnReloaded;
        _lifetime.Cancel();
        lock (_menuGate)
        {
            foreach (var registration in _playerMenus.Values)
                registration.Dispose();
            _playerMenus.Clear();
        }

        foreach (var command in _commands)
            command.Dispose();
        _provider.Dispose();
        _lifetime.Dispose();
    }

    private sealed record TagOption(string Text, PermissionId Permission);
}
