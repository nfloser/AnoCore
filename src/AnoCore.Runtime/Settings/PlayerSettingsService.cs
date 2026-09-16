using System.Text.Json;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Runtime.Settings;

public sealed class PlayerSettingsService : IPlayerSettingsService
{
    private static readonly ModuleId SettingsModule = new("settings");
    private readonly IModuleDataStore _store;
    private readonly JsonSerializerOptions _serializerOptions;

    public PlayerSettingsService(IModuleDataStore store, JsonSerializerOptions? serializerOptions = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public async ValueTask<T> GetAsync<T>(
        PlayerId playerId,
        PlayerSettingKey<T> key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var json = await _store.GetAsync(SettingsModule, BuildKey(playerId, key.Name), cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return key.DefaultValue;
        }

        var value = JsonSerializer.Deserialize<T>(json, _serializerOptions);
        return value is null ? key.DefaultValue : value;
    }

    public ValueTask SetAsync<T>(
        PlayerId playerId,
        PlayerSettingKey<T> key,
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var json = JsonSerializer.Serialize(value, _serializerOptions);
        return _store.SetAsync(SettingsModule, BuildKey(playerId, key.Name), json, cancellationToken);
    }

    public ValueTask<bool> ResetAsync<T>(
        PlayerId playerId,
        PlayerSettingKey<T> key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _store.DeleteAsync(SettingsModule, BuildKey(playerId, key.Name), cancellationToken);
    }

    private static string BuildKey(PlayerId playerId, string settingName)
        => $"player:{playerId.Value}:setting:{settingName}";
}
