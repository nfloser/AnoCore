using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Hud;

public interface ICustomHudService
{
    IDisposable Register(
        ModuleId owner,
        CustomHudDefinition definition,
        CustomHudClickHandler? clickHandler = null);

    bool Show(PlayerId playerId, CustomHudId hudId);

    bool Hide(PlayerId playerId, CustomHudId hudId);

    void HideAll(CustomHudId hudId);

    bool SetText(
        PlayerId playerId,
        CustomHudId hudId,
        string panelId,
        string value,
        string variableName = "text");

    bool SetClass(
        PlayerId playerId,
        CustomHudId hudId,
        string panelId,
        string className,
        bool enabled);
}
