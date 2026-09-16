using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Persistence;

public interface IPlayerRepository
{
    ValueTask<PlayerProfile?> GetAsync(PlayerId id, CancellationToken cancellationToken = default);

    ValueTask UpsertAsync(PlayerProfile profile, CancellationToken cancellationToken = default);
}
