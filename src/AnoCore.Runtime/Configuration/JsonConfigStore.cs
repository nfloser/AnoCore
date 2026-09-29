using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AnoCore.Abstractions.Configuration;

namespace AnoCore.Runtime.Configuration;

public sealed class JsonConfigStore : IConfigStore, IVersionedConfigStore
{
    private const int MaximumMigrations = 128;
    private static readonly Regex ValidName = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _rootPath;
    private readonly JsonSerializerOptions _options;

    public JsonConfigStore(string rootPath, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("A configuration root path is required.", nameof(rootPath));
        _rootPath = Path.GetFullPath(rootPath);
        _options = options ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };
    }

    public async ValueTask<T> LoadAsync<T>(
        string name, Func<T> createDefault,
        Func<T, IReadOnlyCollection<string>>? validate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createDefault);
        var path = GetPath(name);
        if (!File.Exists(path))
        {
            var defaults = createDefault();
            Validate(name, defaults, validate);
            await SaveAsync(name, defaults, validate, cancellationToken).ConfigureAwait(false);
            return defaults;
        }

        var value = await ReadAsync<T>(name, path, cancellationToken).ConfigureAwait(false);
        Validate(name, value, validate);
        return value;
    }

    public async ValueTask<T> LoadVersionedAsync<T>(
        string name, int currentVersion, Func<T> createDefault,
        IReadOnlyCollection<ConfigMigration<T>> migrations,
        Func<T, IReadOnlyCollection<string>>? validate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createDefault);
        ArgumentNullException.ThrowIfNull(migrations);
        if (currentVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(currentVersion), "Current schema version must be positive.");
        if (migrations.Count > MaximumMigrations)
            throw new ArgumentOutOfRangeException(nameof(migrations), $"At most {MaximumMigrations} migrations are allowed.");

        var steps = new Dictionary<int, ConfigMigration<T>>();
        foreach (var migration in migrations)
        {
            ArgumentNullException.ThrowIfNull(migration);
            if (migration.FromVersion >= currentVersion)
                throw new ArgumentException("Migration source versions must be below the current version.", nameof(migrations));
            if (!steps.TryAdd(migration.FromVersion, migration))
                throw new ArgumentException("Migration source versions must be unique.", nameof(migrations));
        }

        var path = GetPath(name);
        if (!File.Exists(path))
        {
            var defaults = createDefault();
            Validate(name, defaults, validate);
            await SaveVersionedAsync(name, currentVersion, defaults, cancellationToken).ConfigureAwait(false);
            return defaults;
        }

        using var document = await ReadDocumentAsync(name, path, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var versionElement = default(JsonElement);
        var valueElement = default(JsonElement);
        var hasVersion = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("$schemaVersion", out versionElement);
        var hasValue = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("value", out valueElement);
        if (hasVersion && !hasValue)
            throw new ConfigStoreException($"Configuration '{name}' contains an incomplete version envelope.");

        var sourceVersion = hasVersion ? ReadVersion(name, versionElement) : 0;
        if (sourceVersion > currentVersion)
            throw new ConfigStoreException(
                $"Configuration '{name}' uses future schema version {sourceVersion}; supported version is {currentVersion}.");

        var source = hasValue ? valueElement : root;
        var value = Deserialize<T>(name, source);
        for (var version = sourceVersion; version < currentVersion; version++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!steps.TryGetValue(version, out var migration))
                throw new ConfigMigrationException(name, version, version + 1, "No migration step is registered.");
            try
            {
                value = migration.Apply(value)
                    ?? throw new InvalidOperationException("The migration returned null.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new ConfigMigrationException(name, version, version + 1, "The migration failed.", exception);
            }
        }

        Validate(name, value, validate);
        if (sourceVersion != currentVersion)
            await SaveVersionedAsync(name, currentVersion, value, cancellationToken).ConfigureAwait(false);
        return value;
    }

    public ValueTask SaveAsync<T>(
        string name, T value,
        Func<T, IReadOnlyCollection<string>>? validate = null,
        CancellationToken cancellationToken = default)
    {
        Validate(name, value, validate);
        return WriteAsync(name, value, cancellationToken);
    }

    private ValueTask SaveVersionedAsync<T>(
        string name, int version, T value, CancellationToken cancellationToken)
        => WriteAsync(name, new VersionedConfigEnvelope<T>(version, value), cancellationToken);

    private async ValueTask<T> ReadAsync<T>(
        string name, string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = OpenRead(path);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, _options, cancellationToken)
                .ConfigureAwait(false);
            return value ?? throw new ConfigStoreException($"Configuration '{name}' deserialized to null.");
        }
        catch (ConfigStoreException) { throw; }
        catch (JsonException exception)
        {
            throw new ConfigStoreException($"Configuration '{name}' contains invalid JSON.", exception);
        }
        catch (IOException exception)
        {
            throw new ConfigStoreException($"Configuration '{name}' could not be read.", exception);
        }
    }

    private async ValueTask<JsonDocument> ReadDocumentAsync(
        string name, string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = OpenRead(path);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new ConfigStoreException($"Configuration '{name}' contains invalid JSON.", exception);
        }
        catch (IOException exception)
        {
            throw new ConfigStoreException($"Configuration '{name}' could not be read.", exception);
        }
    }

    private async ValueTask WriteAsync<T>(
        string name, T value, CancellationToken cancellationToken)
    {
        var path = GetPath(name);
        Directory.CreateDirectory(_rootPath);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, _options, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (IOException exception)
        {
            throw new ConfigStoreException(
                $"Configuration '{name}' could not be written atomically.", exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private string GetPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !ValidName.IsMatch(name.Trim()))
            throw new ArgumentException(
                "Configuration names may contain only letters, numbers, dots, underscores and hyphens.",
                nameof(name));
        return Path.Combine(_rootPath, $"{name.Trim()}.json");
    }

    private static FileStream OpenRead(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);

    private T Deserialize<T>(string name, JsonElement element)
    {
        try
        {
            return element.Deserialize<T>(_options)
                ?? throw new ConfigStoreException($"Configuration '{name}' deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ConfigStoreException($"Configuration '{name}' contains invalid JSON.", exception);
        }
    }

    private static int ReadVersion(string name, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out var version)
            || version < 0)
            throw new ConfigStoreException($"Configuration '{name}' contains an invalid schema version.");
        return version;
    }

    private static void Validate<T>(
        string name, T value, Func<T, IReadOnlyCollection<string>>? validate)
    {
        if (validate is null)
            return;
        var errors = validate(value)
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Select(error => error.Trim())
            .ToArray();
        if (errors.Length > 0)
            throw new ConfigValidationException(name, errors);
    }

    private sealed record VersionedConfigEnvelope<T>(
        [property: JsonPropertyName("$schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("value")] T Value);
}
