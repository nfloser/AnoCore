using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Runtime.Menus;

public sealed class MenuService : IMenuService
{
    public event Action<PlayerId>? Changed;
    private readonly Dictionary<PlayerId, long> _revisions = [];
    private long _nextRevision;
    private readonly object _gate = new();
    private readonly Dictionary<MenuId, Registration> _menus = [];
    private readonly Dictionary<PlayerId, MenuId> _openMenus = [];

    public IDisposable Register(ModuleId owner, MenuDefinition menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        var registration = new Registration(owner, menu);

        lock (_gate)
        {
            if (!_menus.TryAdd(menu.Id, registration))
            {
                throw new InvalidOperationException($"Menu '{menu.Id}' is already registered.");
            }
        }

        return new RegistrationHandle(this, menu.Id, registration);
    }

    public void UnregisterAll(ModuleId owner)
    {
        PlayerId[] affected;
        lock (_gate)
        {
            affected = _openMenus.Where(pair => _menus[pair.Value].Owner == owner).Select(pair => pair.Key).ToArray();
            foreach (var id in _menus.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key).ToArray())
            {
                RemoveMenuUnsafe(id);
            }
        }
        foreach (var player in affected) NotifyChanged(player);
    }

    public void Open(PlayerId playerId, MenuId menuId)
    {
        lock (_gate)
        {
            if (!_menus.ContainsKey(menuId))
            {
                throw new KeyNotFoundException($"Menu '{menuId}' is not registered.");
            }

            _openMenus[playerId] = menuId;
            _revisions[playerId] = ++_nextRevision;
        }
        NotifyChanged(playerId);
    }

    public bool Close(PlayerId playerId)
    {
        bool closed;
        lock (_gate)
        {
            closed = _openMenus.Remove(playerId);
            _revisions.Remove(playerId);
        }
        if (closed) NotifyChanged(playerId);
        return closed;
    }

    public long GetOpenRevision(PlayerId playerId)
    {
        lock (_gate) return _revisions.GetValueOrDefault(playerId);
    }

    private void NotifyChanged(PlayerId playerId)
    {
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try { ((Action<PlayerId>)handler)(playerId); }
            catch { /* Observers cannot roll back an accepted menu mutation. */ }
        }
    }

    public bool TryGetOpenMenu(PlayerId playerId, out MenuDefinition? menu)
    {
        lock (_gate)
        {
            if (_openMenus.TryGetValue(playerId, out var id) && _menus.TryGetValue(id, out var registration))
            {
                menu = registration.Menu;
                return true;
            }

            menu = null;
            return false;
        }
    }

    public ValueTask<MenuSelectionResult> SelectAsync(
        PlayerId playerId,
        string optionId,
        CancellationToken cancellationToken = default)
        => SelectCoreAsync(playerId, null, optionId, cancellationToken);

    public ValueTask<MenuSelectionResult> SelectAsync(
        PlayerId playerId,
        MenuDefinition expectedMenu,
        string optionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedMenu);
        return SelectCoreAsync(playerId, expectedMenu, optionId, cancellationToken);
    }

    private async ValueTask<MenuSelectionResult> SelectCoreAsync(
        PlayerId playerId,
        MenuDefinition? expectedMenu,
        string optionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(optionId))
        {
            return MenuSelectionResult.Rejected("A menu option is required.");
        }

        MenuDefinition? menu;
        MenuOption? option;
        var closed = false;
        lock (_gate)
        {
            if (!_openMenus.TryGetValue(playerId, out var menuId)
                || !_menus.TryGetValue(menuId, out var registration))
            {
                return MenuSelectionResult.Rejected("No menu is open for this player.");
            }

            menu = registration.Menu;
            if (expectedMenu is not null && !ReferenceEquals(expectedMenu, menu))
                return MenuSelectionResult.Rejected("This menu is no longer open.");

            option = menu.Options.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, optionId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (option is null)
            {
                return MenuSelectionResult.Rejected("The selected menu option does not exist.");
            }

            if (!option.KeepOpen)
            {
                _openMenus.Remove(playerId);
                _revisions.Remove(playerId);
                closed = true;
            }
        }
        if (closed) NotifyChanged(playerId);

        cancellationToken.ThrowIfCancellationRequested();
        await option.OnSelected(new MenuSelectionContext(playerId, menu.Id, option.Id, cancellationToken)).ConfigureAwait(false);
        return MenuSelectionResult.Success();
    }

    private void Unregister(MenuId id, Registration expected)
    {
        PlayerId[] affected = [];
        lock (_gate)
        {
            if (_menus.TryGetValue(id, out var current) && ReferenceEquals(current, expected))
            {
                affected = _openMenus.Where(pair => pair.Value == id).Select(pair => pair.Key).ToArray();
                RemoveMenuUnsafe(id);
            }
        }
        foreach (var player in affected) NotifyChanged(player);
    }

    private void RemoveMenuUnsafe(MenuId id)
    {
        _menus.Remove(id);
        foreach (var player in _openMenus.Where(pair => pair.Value == id).Select(pair => pair.Key).ToArray())
        {
            _openMenus.Remove(player);
            _revisions.Remove(player);
        }
    }

    private sealed class Registration(ModuleId owner, MenuDefinition menu)
    {
        public ModuleId Owner { get; } = owner;

        public MenuDefinition Menu { get; } = menu;
    }

    private sealed class RegistrationHandle : IDisposable
    {
        private MenuService? _service;
        private readonly MenuId _id;
        private readonly Registration _registration;

        public RegistrationHandle(MenuService service, MenuId id, Registration registration)
        {
            _service = service;
            _id = id;
            _registration = registration;
        }

        public void Dispose() => Interlocked.Exchange(ref _service, null)?.Unregister(_id, _registration);
    }
}
