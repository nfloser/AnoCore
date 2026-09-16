using System.Data.Common;

namespace AnoCore.Abstractions.Persistence;

public interface IDatabaseMigration
{
    long Version { get; }

    string Name { get; }

    ValueTask ApplyAsync(
        DbConnection connection,
        CancellationToken cancellationToken);
}
