using System.Text.RegularExpressions;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Modules;

namespace AnoCore.Runtime.Configuration;

public sealed class ConfigReloadRegistry : IConfigReloadRegistry
{
    private const int MaximumRegistrations = 128;
    private static readonly Regex ValidName = new(
        "^[a-z0-9][a-z0-9._-]{0,127}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly object _sync = new();
    private readonly Dictionary<string, IReloadEntry> _entries = new(StringComparer.Ordinal);

    public IReadOnlyCollection<ConfigReloadDescriptor> Configurations
    {
        get
        {
            lock (_sync)
                return _entries.Values.Select(entry => entry.Descriptor)
                    .OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();
        }
    }

    public IConfigReloadRegistration<T> Register<T>(
        ModuleId owner,
        string name,
        T initialValue,
        Func<CancellationToken, ValueTask<T>> load,
        Func<T, IReadOnlyCollection<string>>? validate = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(load);
        var normalized = NormalizeName(name);
        Validate(normalized, initialValue, validate);
        ReloadEntry<T>? entry = null;
        lock (_sync)
        {
            if (_entries.Count >= MaximumRegistrations)
                throw new InvalidOperationException($"At most {MaximumRegistrations} reloadable configurations may be registered.");
            if (_entries.ContainsKey(normalized))
                throw new InvalidOperationException($"Configuration '{normalized}' is already registered.");
            entry = new ReloadEntry<T>(
                new ConfigReloadDescriptor(normalized, owner),
                initialValue,
                load,
                validate,
                () => Remove(normalized, entry!));
            _entries.Add(normalized, entry);
        }

        return entry;
    }

    public ValueTask ReloadAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeName(name);
        IReloadEntry entry;
        lock (_sync)
        {
            if (!_entries.TryGetValue(normalized, out entry!))
                throw new KeyNotFoundException($"Configuration '{normalized}' is not registered.");
        }

        return entry.ReloadAsync(cancellationToken);
    }

    private void Remove(string name, IReloadEntry entry)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(name, out var current) && ReferenceEquals(current, entry))
                _entries.Remove(name);
        }
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A configuration name is required.", nameof(name));
        var normalized = name.Trim();
        if (!ValidName.IsMatch(normalized))
            throw new ArgumentException(
                "Configuration names may contain lowercase letters, numbers, dots, underscores and hyphens.",
                nameof(name));
        return normalized;
    }

    private static void Validate<T>(
        string name,
        T value,
        Func<T, IReadOnlyCollection<string>>? validate)
    {
        if (value is null)
            throw new ConfigValidationException(name, ["Value cannot be null."]);
        if (validate is null)
            return;
        var errors = validate(value).Where(error => !string.IsNullOrWhiteSpace(error))
            .Select(error => error.Trim()).ToArray();
        if (errors.Length > 0)
            throw new ConfigValidationException(name, errors);
    }

    private interface IReloadEntry
    {
        ConfigReloadDescriptor Descriptor { get; }

        ValueTask ReloadAsync(CancellationToken cancellationToken);
    }

    private sealed class ReloadEntry<T> : IReloadEntry, IConfigReloadRegistration<T>
    {
        private readonly object _sync = new();
        private readonly SemaphoreSlim _reload = new(1, 1);
        private readonly Func<CancellationToken, ValueTask<T>> _load;
        private readonly Func<T, IReadOnlyCollection<string>>? _validate;
        private readonly Action _remove;
        private T _current;
        private int _disposed;

        public ReloadEntry(
            ConfigReloadDescriptor descriptor,
            T initialValue,
            Func<CancellationToken, ValueTask<T>> load,
            Func<T, IReadOnlyCollection<string>>? validate,
            Action remove)
        {
            Descriptor = descriptor;
            _current = initialValue;
            _load = load;
            _validate = validate;
            _remove = remove;
        }

        public ConfigReloadDescriptor Descriptor { get; }

        public T Current
        {
            get
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                lock (_sync)
                    return _current;
            }
        }

        public async ValueTask ReloadAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            await _reload.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                var candidate = await _load(cancellationToken).ConfigureAwait(false);
                Validate(Descriptor.Name, candidate, _validate);
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                lock (_sync)
                    _current = candidate;
            }
            finally
            {
                _reload.Release();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _remove();
        }
    }
}
