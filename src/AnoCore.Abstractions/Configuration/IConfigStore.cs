namespace AnoCore.Abstractions.Configuration;

public interface IConfigStore
{
    ValueTask<T> LoadAsync<T>(
        string name,
        Func<T> createDefault,
        Func<T, IReadOnlyCollection<string>>? validate = null,
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync<T>(
        string name,
        T value,
        Func<T, IReadOnlyCollection<string>>? validate = null,
        CancellationToken cancellationToken = default);
}
