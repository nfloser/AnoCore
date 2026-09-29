namespace AnoCore.Abstractions.Configuration;

public sealed record ConfigMigration<T>
{
    public ConfigMigration(int fromVersion, Func<T, T> apply)
    {
        if (fromVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(fromVersion));
        FromVersion = fromVersion;
        Apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    public int FromVersion { get; }

    public Func<T, T> Apply { get; }
}
