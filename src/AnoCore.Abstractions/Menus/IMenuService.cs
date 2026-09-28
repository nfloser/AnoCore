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

    async ValueTask<MenuSelectionResult> SelectAsync(
        PlayerId playerId,
        MenuDefinition expectedMenu,
        string optionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedMenu);
        if (!TryGetOpenMenu(playerId, out var current)
            || !ReferenceEquals(current, expectedMenu))
            return MenuSelectionResult.Rejected("This menu is no longer open.");

        return await SelectAsync(playerId, optionId, cancellationToken)
            .ConfigureAwait(false);
    }

    ValueTask<MenuSelectionResult> SelectAsync(
        PlayerId playerId,
        string optionId,
        CancellationToken cancellationToken = default);
}
