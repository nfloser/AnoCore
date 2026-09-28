using AnoCore.Abstractions.Modules;

namespace AnoCore.Abstractions.Settings;

public interface IPlayerToggleCatalog
{
    IDisposable Register(ModuleId owner, PlayerToggleSetting setting);

    void UnregisterAll(ModuleId owner);

    IReadOnlyList<PlayerToggleSetting> GetAll();

    bool TryGet(string name, out PlayerToggleSetting? setting);
}
