using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class MySqlSeasonRepository : ISeasonRepository
{
    public MySqlSeasonRepository(IDatabase database)
        => ArgumentNullException.ThrowIfNull(database);

    public ValueTask<IReadOnlyList<PersistedSeason>> ListEffectiveAsync(
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public ValueTask<PersistedSeason?> ReadAsync(
        string seasonId,
        int version,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public ValueTask<SeasonAcceptResult> AcceptAsync(
        SeasonDefinition definition,
        DateTimeOffset acceptedAtUtc,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public ValueTask<PersistedSeason> CloseAsync(
        string seasonId,
        int version,
        DateTimeOffset closedAtUtc,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
