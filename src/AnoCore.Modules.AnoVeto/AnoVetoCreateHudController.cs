using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.AnoVeto;

/// <summary>
/// Native ANOMEME entry menu. Only the Random action publishes a vote at this stage;
/// Manual and Saved are withheld until their complete screen/persistence integrations land.
/// </summary>
public sealed class AnoVetoCreateHudController : IDisposable
{
    public static readonly CustomHudId HudId = new("ano.veto.create");
    public const string LayoutResource = "panorama/layout/custom_game/anocore/ano_veto_create.xml";
    private static readonly ModuleId Owner = new("ano.veto");
    private readonly ICustomHudService _hud;
    private readonly Func<PlayerId, CancellationToken, ValueTask<bool>> _startRandom;
    private readonly IDisposable _registration;

    public AnoVetoCreateHudController(
        ICustomHudService hud,
        Func<PlayerId, CancellationToken, ValueTask<bool>> startRandom)
    {
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
        _startRandom = startRandom ?? throw new ArgumentNullException(nameof(startRandom));
        _registration = _hud.Register(Owner,
            new CustomHudDefinition(HudId, LayoutResource, "ano_veto_create_root",
                ["ano_veto_create_close", "ano_veto_create_random", "ano_veto_create_manual", "ano_veto_create_saved"],
                captureInput: true),
            OnClickAsync);
    }

    public bool Show(PlayerId player)
    {
        ArgumentNullException.ThrowIfNull(player);
        _hud.SetText(player, HudId, "ano_veto_create_status", "Choose how to build your map vote");
        return _hud.Show(player, HudId);
    }

    public void Hide(PlayerId player) => _hud.Hide(player, HudId);

    private async ValueTask OnClickAsync(CustomHudClickContext context)
    {
        if (context.HudId != HudId) return;
        switch (context.ButtonId)
        {
            case "ano_veto_create_close":
                Hide(context.PlayerId);
                break;
            case "ano_veto_create_random":
                if (await _startRandom(context.PlayerId, context.CancellationToken).ConfigureAwait(false))
                    Hide(context.PlayerId);
                else
                    _hud.SetText(context.PlayerId, HudId, "ano_veto_create_status",
                        "Unable to start vote. Check permission and active votes.");
                break;
            case "ano_veto_create_manual":
            case "ano_veto_create_saved":
                _hud.SetText(context.PlayerId, HudId, "ano_veto_create_status",
                    "This option requires the next map picker / preset integration.");
                break;
        }
    }

    public void Dispose()
    {
        _hud.HideAll(HudId);
        _registration.Dispose();
    }
}
