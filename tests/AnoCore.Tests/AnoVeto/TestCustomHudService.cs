using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Tests.AnoVeto;

internal sealed class TestCustomHudService : ICustomHudService
{
    private readonly Dictionary<CustomHudId, RegistrationState> _registrations = [];

    public IReadOnlyCollection<PlayerId> VisiblePlayers(CustomHudId hudId)
        => _registrations.TryGetValue(hudId, out var registration)
            ? registration.Visible.ToArray()
            : [];

    public CustomHudDefinition? Definition(CustomHudId hudId)
        => _registrations.TryGetValue(hudId, out var registration)
            ? registration.Definition
            : null;

    public IDisposable Register(
        ModuleId owner,
        CustomHudDefinition definition,
        CustomHudClickHandler? clickHandler = null)
    {
        _ = owner;
        if (_registrations.ContainsKey(definition.Id))
        {
            throw new InvalidOperationException($"HUD '{definition.Id}' is already registered.");
        }

        var registration = new RegistrationState(definition, clickHandler);
        _registrations.Add(definition.Id, registration);
        return new DisposableAction(() => _registrations.Remove(definition.Id));
    }

    public bool Show(PlayerId playerId, CustomHudId hudId)
    {
        if (!_registrations.TryGetValue(hudId, out var registration))
        {
            return false;
        }

        registration.Visible.Add(playerId);
        return true;
    }

    public bool Hide(PlayerId playerId, CustomHudId hudId)
    {
        if (!_registrations.TryGetValue(hudId, out var registration))
        {
            return false;
        }

        registration.Visible.Remove(playerId);
        return true;
    }

    public void HideAll(CustomHudId hudId)
    {
        if (_registrations.TryGetValue(hudId, out var registration))
        {
            registration.Visible.Clear();
        }
    }

    public bool SetText(
        PlayerId playerId,
        CustomHudId hudId,
        string panelId,
        string value,
        string variableName = "text")
    {
        if (!_registrations.TryGetValue(hudId, out var registration))
        {
            return false;
        }

        registration.Text[(playerId, panelId, variableName)] = value;
        return true;
    }

    public bool SetClass(
        PlayerId playerId,
        CustomHudId hudId,
        string panelId,
        string className,
        bool enabled)
    {
        if (!_registrations.TryGetValue(hudId, out var registration))
        {
            return false;
        }

        registration.Classes[(playerId, panelId, className)] = enabled;
        return true;
    }

    public async ValueTask ClickAsync(PlayerId playerId, CustomHudId hudId, string buttonId)
    {
        if (!_registrations.TryGetValue(hudId, out var registration)
            || registration.ClickHandler is null)
        {
            throw new InvalidOperationException($"HUD '{hudId}' has no click handler.");
        }

        await registration.ClickHandler(
            new CustomHudClickContext(playerId, hudId, buttonId, CancellationToken.None));
    }

    private sealed class RegistrationState(
        CustomHudDefinition definition,
        CustomHudClickHandler? clickHandler)
    {
        public CustomHudDefinition Definition { get; } = definition;
        public CustomHudClickHandler? ClickHandler { get; } = clickHandler;
        public HashSet<PlayerId> Visible { get; } = [];
        public Dictionary<(PlayerId Player, string Panel, string Variable), string> Text { get; } = [];
        public Dictionary<(PlayerId Player, string Panel, string ClassName), bool> Classes { get; } = [];
    }

    private sealed class DisposableAction(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
            => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
