using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Settings;

public interface IPlayerSettingsService
{
    ValueTask<T> GetAsync<T>(
        PlayerId playerId,
        PlayerSettingKey<T> key,
        CancellationToken cancellationToken = default);

    ValueTask SetAsync<T>(
        PlayerId playerId,
        PlayerSettingKey<T> key,
        T value,
        CancellationToken cancellationToken = default);

    ValueTask<bool> ResetAsync<T>(
        PlayerId playerId,
        PlayerSettingKey<T> key,
        CancellationToken cancellationToken = default);
}
