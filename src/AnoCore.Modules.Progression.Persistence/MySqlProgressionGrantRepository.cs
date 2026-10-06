using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Progression.Persistence;

public static class ProgressionPersistenceBootstrap
{
    public static ValueTask EnsureReadyAsync(
        IDatabase database,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

public sealed class MySqlProgressionGrantRepository : IProgressionGrantRepository
{
    public MySqlProgressionGrantRepository(IDatabase database)
        => ArgumentNullException.ThrowIfNull(database);

    public ValueTask<ProgressionLifetimeState> ReadLifetimeAsync(
        PlayerId playerId,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public ValueTask<ProgressionGrantRecord?> ReadGrantAsync(
        PlayerId playerId,
        string grantId,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public ValueTask<ProgressionGrantCommitResult> ApplyAsync(
        PlayerId playerId,
        ProgressionGrantCandidate candidate,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
