using System.Text.RegularExpressions;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Placeholders;

namespace AnoCore.Runtime.Placeholders;

public sealed class PlaceholderRegistry : IPlaceholderRegistry
{
    private static readonly Regex ValidName = new(
        "^[a-z0-9][a-z0-9._-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TokenPattern = new(
        "\\{(?<name>[A-Za-z0-9][A-Za-z0-9._-]{0,63})\\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public IDisposable Register(ModuleId owner, string name, PlaceholderResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(resolver);
        var normalized = NormalizeName(name);
        var entry = new Entry(Guid.NewGuid(), owner, resolver);

        lock (_sync)
        {
            if (_entries.ContainsKey(normalized))
            {
                throw new InvalidOperationException($"Placeholder '{normalized}' is already registered.");
            }

            _entries[normalized] = entry;
        }

        return new Registration(() => Remove(normalized, entry.Id));
    }

    public bool Contains(string name)
    {
        var normalized = NormalizeName(name);
        lock (_sync)
        {
            return _entries.ContainsKey(normalized);
        }
    }

    public int RemoveOwner(ModuleId owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_sync)
        {
            var names = _entries
                .Where(pair => pair.Value.Owner == owner)
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var name in names)
            {
                _entries.Remove(name);
            }

            return names.Length;
        }
    }

    public async ValueTask<string> ResolveAsync(
        string template,
        PlaceholderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var matches = TokenPattern.Matches(template);
        if (matches.Count == 0)
        {
            return template;
        }

        var result = template;
        var names = matches
            .Select(match => match.Groups["name"].Value.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entry? entry;
            lock (_sync)
            {
                _entries.TryGetValue(name, out entry);
            }

            if (entry is null)
            {
                continue;
            }

            var value = await entry.Resolver(context, cancellationToken).ConfigureAwait(false);
            result = result.Replace(
                $"{{{name}}}",
                value ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A placeholder name is required.", nameof(name));
        }

        var normalized = name.Trim().ToLowerInvariant();
        if (!ValidName.IsMatch(normalized))
        {
            throw new ArgumentException(
                "Placeholder names may contain lowercase letters, numbers, dots, underscores and hyphens.",
                nameof(name));
        }

        return normalized;
    }

    private void Remove(string name, Guid id)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(name, out var entry) && entry.Id == id)
            {
                _entries.Remove(name);
            }
        }
    }

    private sealed record Entry(Guid Id, ModuleId Owner, PlaceholderResolver Resolver);

    private sealed class Registration : IDisposable
    {
        private readonly Action _remove;
        private int _disposed;

        public Registration(Action remove)
        {
            _remove = remove;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _remove();
            }
        }
    }
}
