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
    private readonly Dictionary<string, Entry> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<PriorityEntry>> _prioritized =
        new(StringComparer.OrdinalIgnoreCase);

    public IDisposable Register(ModuleId owner, string name, PlaceholderResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(resolver);
        var normalized = NormalizeName(name);
        var entry = new Entry(Guid.NewGuid(), owner, resolver);

        lock (_sync)
        {
            if (_entries.ContainsKey(normalized)
                || _prioritized.ContainsKey(normalized))
            {
                throw new InvalidOperationException(
                    $"Placeholder '{normalized}' is already registered.");
            }

            _entries[normalized] = entry;
        }

        return new Registration(() => Remove(normalized, entry.Id));
    }

    public IDisposable RegisterPrioritized(
        ModuleId owner,
        string name,
        int priority,
        PlaceholderResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(resolver);
        var normalized = NormalizeName(name);
        var entry = new PriorityEntry(Guid.NewGuid(), owner, priority, resolver);

        lock (_sync)
        {
            if (_entries.ContainsKey(normalized))
            {
                throw new InvalidOperationException(
                    $"Placeholder '{normalized}' has exclusive ownership.");
            }

            if (!_prioritized.TryGetValue(normalized, out var entries))
            {
                entries = [];
                _prioritized[normalized] = entries;
            }

            if (entries.Any(candidate => candidate.Priority == priority))
            {
                throw new InvalidOperationException(
                    $"Placeholder '{normalized}' already has priority {priority}.");
            }

            entries.Add(entry);
            entries.Sort((left, right) => right.Priority.CompareTo(left.Priority));
        }

        return new Registration(() => RemovePrioritized(normalized, entry.Id));
    }

    public bool Contains(string name)
    {
        var normalized = NormalizeName(name);
        lock (_sync)
        {
            return _entries.ContainsKey(normalized)
                || _prioritized.ContainsKey(normalized);
        }
    }

    public int RemoveOwner(ModuleId owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_sync)
        {
            var exclusiveNames = _entries
                .Where(pair => pair.Value.Owner == owner)
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var name in exclusiveNames)
                _entries.Remove(name);

            var removed = exclusiveNames.Length;
            foreach (var pair in _prioritized.ToArray())
            {
                removed += pair.Value.RemoveAll(entry => entry.Owner == owner);
                if (pair.Value.Count == 0)
                    _prioritized.Remove(pair.Key);
            }

            return removed;
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
            return template;

        var result = template;
        var names = matches
            .Select(match => match.Groups["name"].Value.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entry? exclusive;
            PriorityEntry[]? prioritized;
            lock (_sync)
            {
                _entries.TryGetValue(name, out exclusive);
                prioritized = _prioritized.TryGetValue(name, out var entries)
                    ? entries.ToArray()
                    : null;
            }

            if (exclusive is null && prioritized is null)
                continue;

            string? value = null;
            if (exclusive is not null)
            {
                value = await exclusive.Resolver(context, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                foreach (var candidate in prioritized!)
                {
                    value = await candidate.Resolver(context, cancellationToken)
                        .ConfigureAwait(false);
                    if (value is not null)
                        break;
                }
            }

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
            throw new ArgumentException("A placeholder name is required.", nameof(name));

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
                _entries.Remove(name);
        }
    }

    private void RemovePrioritized(string name, Guid id)
    {
        lock (_sync)
        {
            if (!_prioritized.TryGetValue(name, out var entries))
                return;
            entries.RemoveAll(entry => entry.Id == id);
            if (entries.Count == 0)
                _prioritized.Remove(name);
        }
    }

    private sealed record Entry(
        Guid Id,
        ModuleId Owner,
        PlaceholderResolver Resolver);

    private sealed record PriorityEntry(
        Guid Id,
        ModuleId Owner,
        int Priority,
        PlaceholderResolver Resolver);

    private sealed class Registration : IDisposable
    {
        private readonly Action _remove;
        private int _disposed;

        public Registration(Action remove) => _remove = remove;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _remove();
        }
    }
}
