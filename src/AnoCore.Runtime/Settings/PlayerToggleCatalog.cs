using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Settings;

namespace AnoCore.Runtime.Settings;

public sealed class PlayerToggleCatalog : IPlayerToggleCatalog
{
    private const int MaxSettings = 64;
    private readonly object _gate = new();
    private readonly Dictionary<string, Registration> _settings = new(StringComparer.Ordinal);

    public IDisposable Register(ModuleId owner, PlayerToggleSetting setting)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(setting);
        lock (_gate)
        {
            if (_settings.ContainsKey(setting.Key.Name))
                throw new InvalidOperationException(
                    $"Setting '{setting.Key.Name}' is already registered.");
            if (_settings.Count >= MaxSettings)
                throw new InvalidOperationException("The setting catalog is full.");

            var registration = new Registration(owner, setting);
            _settings.Add(setting.Key.Name, registration);
            return new RegistrationHandle(this, setting.Key.Name, registration);
        }
    }

    public void UnregisterAll(ModuleId owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_gate)
        {
            foreach (var name in _settings
                .Where(pair => pair.Value.Owner == owner)
                .Select(pair => pair.Key).ToArray())
                _settings.Remove(name);
        }
    }

    public IReadOnlyList<PlayerToggleSetting> GetAll()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_settings
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value.Setting).ToArray());
        }
    }

    public bool TryGet(string name, out PlayerToggleSetting? setting)
    {
        lock (_gate)
        {
            if (name is not null && _settings.TryGetValue(name, out var registration))
            {
                setting = registration.Setting;
                return true;
            }
            setting = null;
            return false;
        }
    }

    private void Unregister(string name, Registration expected)
    {
        lock (_gate)
        {
            if (_settings.TryGetValue(name, out var current)
                && ReferenceEquals(current, expected))
                _settings.Remove(name);
        }
    }

    private sealed record Registration(ModuleId Owner, PlayerToggleSetting Setting);

    private sealed class RegistrationHandle(
        PlayerToggleCatalog catalog, string name, Registration registration) : IDisposable
    {
        private PlayerToggleCatalog? _catalog = catalog;

        public void Dispose()
            => Interlocked.Exchange(ref _catalog, null)?.Unregister(name, registration);
    }
}
