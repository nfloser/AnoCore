namespace AnoCore.Runtime.Configuration;

public class ConfigStoreException : Exception
{
    public ConfigStoreException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class ConfigValidationException : ConfigStoreException
{
    public ConfigValidationException(string name, IReadOnlyCollection<string> errors)
        : base($"Configuration '{name}' is invalid: {string.Join("; ", errors)}")
    {
        Name = name;
        Errors = errors.ToArray();
    }

    public string Name { get; }

    public IReadOnlyCollection<string> Errors { get; }
}

public sealed class ConfigMigrationException : ConfigStoreException
{
    public ConfigMigrationException(
        string name, int fromVersion, int toVersion, string detail,
        Exception? innerException = null)
        : base(
            $"Configuration '{name}' could not migrate from schema version {fromVersion} to {toVersion}: {detail}",
            innerException)
    {
        Name = name;
        FromVersion = fromVersion;
        ToVersion = toVersion;
    }

    public string Name { get; }

    public int FromVersion { get; }

    public int ToVersion { get; }
}
