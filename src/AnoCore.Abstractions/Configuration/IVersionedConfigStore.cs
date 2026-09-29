namespace AnoCore.Abstractions.Configuration;

public interface IVersionedConfigStore
{
    ValueTask<T> LoadVersionedAsync<T>(
        string name,
        int currentVersion,
        Func<T> createDefault,
        IReadOnlyCollection<ConfigMigration<T>> migrations,
        Func<T, IReadOnlyCollection<string>>? validate = null,
        CancellationToken cancellationToken = default);
}
