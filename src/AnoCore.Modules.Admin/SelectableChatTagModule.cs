using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Placeholders;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Modules.Admin;

public sealed class SelectableChatTagModule : IDisposable
{
    private static readonly ModuleId Owner = new("ano.chat.tags");
    private static readonly PlayerSettingKey<string> SelectedTag = new("chat.tag.selected", "");
    private readonly IPlayerRegistry _players;
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
        Action<Exception>? onFailure)
    {
        _lifetimeToken = _lifetime.Token;
        _players = players;
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
            players, settings, permissions, reloadEvents, refresh, onFailure);
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
        await _settings.SetAsync(player.Id, SelectedTag, selected.ToLowerInvariant(),
            context.CancellationToken).ConfigureAwait(false);
        await RefreshCurrentAsync(player, context.CancellationToken).ConfigureAwait(false);
        return CommandResult.Ok("[ANO] Chat tag selected.");
    }

    private async ValueTask<CommandResult> ClearAsync(CommandContext context)
    {
        if (!TryCurrent(context.Caller, out var player))
            return NoPlayer();
        await _settings.ResetAsync(player!.Id, SelectedTag, context.CancellationToken)
            .ConfigureAwait(false);
        await RefreshCurrentAsync(player, context.CancellationToken).ConfigureAwait(false);
        return CommandResult.Ok("[ANO] Chat tag cleared.");
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
        foreach (var command in _commands)
            command.Dispose();
        _provider.Dispose();
        _lifetime.Dispose();
    }

    private sealed record TagOption(string Text, PermissionId Permission);
}
