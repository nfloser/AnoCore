using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Settings;

public interface IPlayerSettingsResetService
{
    ValueTask<int> ResetAllAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default);
}
