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
