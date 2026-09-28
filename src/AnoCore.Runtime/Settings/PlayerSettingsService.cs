using System.Text.Json;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Runtime.Settings;

public sealed class PlayerSettingsService : IPlayerSettingsService
{
    private static readonly ModuleId SettingsModule = new("settings");
    private readonly IModuleDataStore _store;
    private readonly IAnoEventBus? _events;
    private readonly Action<Exception>? _onEventFailure;
    private readonly JsonSerializerOptions _serializerOptions;

    public PlayerSettingsService(
        IModuleDataStore store,
        JsonSerializerOptions? serializerOptions = null,
        IAnoEventBus? events = null,
        Action<Exception>? onEventFailure = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
        _events = events;
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

    private async ValueTask PublishChangeAsync(
        PlayerId playerId, string name, PlayerSettingChangeKind kind)
    {
        if (_events is null)
            return;
        try
        {
            // The write is already durable; observer cancellation cannot undo it.
            await _events.PublishAsync(
                new PlayerSettingChangedEvent(playerId, name, kind), CancellationToken.None)
                .ConfigureAwait(false);
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
        => $"player.{playerId.SteamId64}.setting.{settingName}";
}
