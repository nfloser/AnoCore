using System.Text.Json;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Runtime.Settings;

public sealed class PlayerSettingsService : IPlayerSettingsService, IPlayerSettingsResetService
{
    private static readonly ModuleId SettingsModule = new("settings");
    private readonly IModuleDataStore _store;
    private readonly IModuleDataPrefixStore? _prefixStore;
    private readonly IAnoEventBus? _events;
    private readonly Action<Exception>? _onEventFailure;
    private readonly JsonSerializerOptions _serializerOptions;

    public PlayerSettingsService(
        IModuleDataStore store, JsonSerializerOptions? serializerOptions = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _prefixStore = store as IModuleDataPrefixStore;
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public PlayerSettingsService(
        IModuleDataStore store,
        JsonSerializerOptions? serializerOptions,
        IAnoEventBus events,
        Action<Exception>? onEventFailure = null)
        : this(store, serializerOptions)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _onEventFailure = onEventFailure;
    }

    public async ValueTask<T> GetAsync<T>(
        PlayerId playerId,
        PlayerSettingKey<T> key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(key);
        var json = await _store.GetAsync(SettingsModule, BuildKey(playerId, key.Name), cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return key.DefaultValue;
        }

        var value = JsonSerializer.Deserialize<T>(json, _serializerOptions);
        return value is null ? key.DefaultValue : value;
    }

    public async ValueTask SetAsync<T>(
        PlayerId playerId,
        PlayerSettingKey<T> key,
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(value, _serializerOptions);
        await _store.SetAsync(SettingsModule, BuildKey(playerId, key.Name), json,
            cancellationToken).ConfigureAwait(false);
        await PublishChangeAsync(playerId, key.Name, PlayerSettingChangeKind.Set)
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> ResetAsync<T>(
        PlayerId playerId,
        PlayerSettingKey<T> key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        var removed = await _store.DeleteAsync(SettingsModule, BuildKey(playerId, key.Name),
            cancellationToken).ConfigureAwait(false);
        if (removed)
            await PublishChangeAsync(playerId, key.Name, PlayerSettingChangeKind.Reset)
                .ConfigureAwait(false);
        return removed;
    }

    public async ValueTask<int> ResetAllAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        cancellationToken.ThrowIfCancellationRequested();
        var prefixStore = _prefixStore ?? throw new NotSupportedException(
            "The configured module data store does not support prefix deletion.");
        var removed = await prefixStore.DeleteByPrefixAsync(
            SettingsModule, BuildPrefix(playerId), cancellationToken).ConfigureAwait(false);
        if (removed > 0)
            await PublishEventAsync(new PlayerSettingsResetEvent(playerId, removed)).ConfigureAwait(false);
        return removed;
    }

    private ValueTask PublishChangeAsync(
        PlayerId playerId, string name, PlayerSettingChangeKind kind)
        => PublishEventAsync(new PlayerSettingChangedEvent(playerId, name, kind));

    private async ValueTask PublishEventAsync<TEvent>(TEvent value)
        where TEvent : IAnoEvent
    {
        if (_events is null)
            return;
        try
        {
            // The write is already durable; observer cancellation cannot undo it.
            await _events.PublishAsync(value, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            try
            {
                _onEventFailure?.Invoke(exception);
            }
            catch (Exception)
            {
                // A failed diagnostic callback must not turn a committed write into a failure.
            }
        }
    }

    private static string BuildKey(PlayerId playerId, string settingName)
        => $"{BuildPrefix(playerId)}{settingName}";

    private static string BuildPrefix(PlayerId playerId)
        => $"player.{playerId.SteamId64}.setting.";
}
