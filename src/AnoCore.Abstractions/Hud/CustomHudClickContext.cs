using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Hud;

public sealed record CustomHudClickContext(
    PlayerId PlayerId,
    CustomHudId HudId,
    string ButtonId,
    CancellationToken CancellationToken);

public delegate ValueTask CustomHudClickHandler(CustomHudClickContext context);
