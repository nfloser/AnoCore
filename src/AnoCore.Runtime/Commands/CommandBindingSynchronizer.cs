using AnoCore.Abstractions.Commands;

namespace AnoCore.Runtime.Commands;

// Invoke from the native dispatch thread; registry snapshots themselves remain thread-safe.
public sealed class CommandBindingSynchronizer(IAnoCommandRegistry registry, Func<CommandDescriptor, IDisposable> bind) : IDisposable
{
    private readonly Dictionary<string, (CommandDescriptor Descriptor, IDisposable Handle)> _bindings = new(StringComparer.Ordinal);
    private bool _disposed;

    public void Synchronize()
    {
        if (_disposed) return;
        var commands = registry.GetCommands().ToDictionary(item => item.Name, StringComparer.Ordinal);
        foreach (var (name, binding) in _bindings.ToArray())
        {
            if (commands.TryGetValue(name, out var current) && ReferenceEquals(current, binding.Descriptor)) continue;
            _bindings.Remove(name);
            binding.Handle.Dispose();
        }
        foreach (var descriptor in commands.Values.OrderBy(item => item.Name, StringComparer.Ordinal))
            if (!_bindings.ContainsKey(descriptor.Name))
                _bindings.Add(descriptor.Name, (descriptor, bind(descriptor)));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        foreach (var binding in _bindings.Values.Reverse())
        {
            try { binding.Handle.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        _bindings.Clear();
        if (failures is not null) throw new AggregateException("Native command binding cleanup was incomplete.", failures);
    }
}
