using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Menus;

public sealed record MenuSelectionContext(
    PlayerId PlayerId,
    MenuId MenuId,
    string OptionId,
    CancellationToken CancellationToken);
