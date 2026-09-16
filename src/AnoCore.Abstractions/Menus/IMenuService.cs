using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Menus;

public interface IMenuService
{
    IDisposable Register(ModuleId owner, MenuDefinition menu);

    void UnregisterAll(ModuleId owner);

    void Open(PlayerId playerId, MenuId menuId);

    bool Close(PlayerId playerId);

    bool TryGetOpenMenu(PlayerId playerId, out MenuDefinition? menu);

    ValueTask<MenuSelectionResult> SelectAsync(
        PlayerId playerId,
        string optionId,
        CancellationToken cancellationToken = default);
}
