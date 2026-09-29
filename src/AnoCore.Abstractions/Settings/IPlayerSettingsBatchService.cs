using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Settings;

public interface IPlayerSettingsBatchService
{
    ValueTask SetManyAsync<T>(
        PlayerId playerId,
        IReadOnlyCollection<PlayerSettingUpdate<T>> updates,
        CancellationToken cancellationToken = default);
}
