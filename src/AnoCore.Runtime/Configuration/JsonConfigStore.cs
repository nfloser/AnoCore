using System.Text.Json;
using System.Text.RegularExpressions;
using AnoCore.Abstractions.Configuration;

namespace AnoCore.Runtime.Configuration;

public sealed class JsonConfigStore : IConfigStore
{
    private static readonly Regex ValidName = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _rootPath;
    private readonly JsonSerializerOptions _options;

    public JsonConfigStore(string rootPath, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A configuration root path is required.", nameof(rootPath));
        }

        _rootPath = Path.GetFullPath(rootPath);
        _options = options ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };
    }

    public async ValueTask<T> LoadAsync<T>(
        string name,
        Func<T> createDefault,
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

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, _options, cancellationToken)
                .ConfigureAwait(false);

            if (value is null)
            {
                throw new ConfigStoreException($"Configuration '{name}' deserialized to null.");
            }

            Validate(name, value, validate);
            return value;
        }
        catch (ConfigStoreException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ConfigStoreException(
                $"Configuration '{name}' contains invalid JSON.",
                exception);
        }
        catch (IOException exception)
        {
            throw new ConfigStoreException(
                $"Configuration '{name}' could not be read.",
                exception);
        }
    }

    public async ValueTask SaveAsync<T>(
        string name,
        T value,
        Func<T, IReadOnlyCollection<string>>? validate = null,
        CancellationToken cancellationToken = default)
    {
        Validate(name, value, validate);
        var path = GetPath(name);
        Directory.CreateDirectory(_rootPath);

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
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
                $"Configuration '{name}' could not be written atomically.",
                exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string GetPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !ValidName.IsMatch(name.Trim()))
        {
            throw new ArgumentException(
                "Configuration names may contain only letters, numbers, dots, underscores and hyphens.",
                nameof(name));
        }

        return Path.Combine(_rootPath, $"{name.Trim()}.json");
    }

    private static void Validate<T>(
        string name,
        T value,
        Func<T, IReadOnlyCollection<string>>? validate)
    {
        if (validate is null)
        {
            return;
        }

        var errors = validate(value)
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Select(error => error.Trim())
            .ToArray();

        if (errors.Length > 0)
        {
            throw new ConfigValidationException(name, errors);
        }
    }
}
