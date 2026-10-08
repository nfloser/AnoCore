using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.AnoVeto;

public sealed class AnoVetoHudController : IDisposable
{
    private static readonly ModuleId Owner = new("ano.veto");
    public static readonly CustomHudId HudId = new("ano.veto");
    public const string LayoutResource = "panorama/layout/custom_game/anocore/ano_veto.xml";

    private const string RootPanelId = "ano_veto_root";
    private const string CloseButtonId = "ano_veto_close";
    private const string MapButtonPrefix = "ano_veto_map_";
    private readonly ICustomHudService _hud;
    private readonly AnoVetoCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;
    private readonly object _previewGate = new();
    private readonly Dictionary<(PlayerId Player, int Index), string> _previewClasses = [];
    private IDisposable? _registration;

    public AnoVetoHudController(
        ICustomHudService hud,
        AnoVetoCoordinator coordinator,
        TimeProvider? timeProvider = null)
    {
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _timeProvider = timeProvider ?? TimeProvider.System;

        var buttonIds = Enumerable.Range(0, 8)
            .Select(index => $"{MapButtonPrefix}{index}")
            .Append(CloseButtonId)
            .ToArray();
        _registration = _hud.Register(
            Owner,
            new CustomHudDefinition(
                HudId,
                LayoutResource,
                RootPanelId,
                buttonIds,
                captureInput: true),
            HandleClickAsync);
    }

    public bool Show(PlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        if (!_coordinator.TryGetStatus(out var maps) || maps.Count != 8)
        {
            return false;
        }

        _hud.SetText(playerId, HudId, "ano_veto_title", "CHOOSE THE NEXT MAP");
        _hud.SetText(playerId, HudId, "ano_veto_status", "Click one map to cast your vote");

        for (var index = 0; index < maps.Count; index++)
        {
            var panelId = $"ano_veto_map_{index}_image";
            var previewClass = AnoVetoMapPreview.GetClass(maps[index]);
            lock (_previewGate)
            {
                if (_previewClasses.TryGetValue((playerId, index), out var previous))
                {
                    _hud.SetClass(playerId, HudId, panelId, previous, false);
                }

                _hud.SetClass(playerId, HudId, panelId, previewClass, true);
                _previewClasses[(playerId, index)] = previewClass;
            }

            _hud.SetText(
                playerId,
                HudId,
                $"ano_veto_map_{index}_text",
                maps[index].DisplayName);
        }

        return _hud.Show(playerId, HudId);
    }

    public void ShowAll(IEnumerable<PlayerId> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        foreach (var playerId in players.Distinct())
        {
            Show(playerId);
        }
    }

    public bool Hide(PlayerId playerId)
    {
        var hidden = _hud.Hide(playerId, HudId);
        ClearPreviews(playerId);
        return hidden;
    }

    public void HideAll()
    {
        _hud.HideAll(HudId);
        ClearPreviews(null);
    }

    private void ClearPreviews(PlayerId? playerId)
    {
        lock (_previewGate)
        {
            foreach (var pair in _previewClasses.Where(pair => playerId is null || pair.Key.Player == playerId).ToArray())
            {
                _hud.SetClass(pair.Key.Player, HudId, $"ano_veto_map_{pair.Key.Index}_image", pair.Value, false);
                _previewClasses.Remove(pair.Key);
            }
        }
    }

    public void Dispose()
    {
        HideAll();
        Interlocked.Exchange(ref _registration, null)?.Dispose();
        lock (_previewGate)
        {
            _previewClasses.Clear();
        }
    }

    private async ValueTask HandleClickAsync(CustomHudClickContext context)
    {
        if (context.HudId != HudId)
        {
            return;
        }

        if (string.Equals(context.ButtonId, CloseButtonId, StringComparison.Ordinal))
        {
            Hide(context.PlayerId);
            return;
        }

        if (!context.ButtonId.StartsWith(MapButtonPrefix, StringComparison.Ordinal)
            || !int.TryParse(context.ButtonId.AsSpan(MapButtonPrefix.Length), out var index)
            || index is < 0 or >= 8
            || !_coordinator.TryGetStatus(out var maps)
            || index >= maps.Count)
        {
            return;
        }

        var result = await _coordinator.CastAsync(
            context.PlayerId,
            maps[index].MapId,
            _timeProvider.GetUtcNow(),
            context.CancellationToken).ConfigureAwait(false);

        if (result.Accepted)
        {
            Hide(context.PlayerId);
            if (result.Outcome != AnoVetoOutcome.None)
            {
                HideAll();
            }

            return;
        }

        if (result.Failure is AnoVetoFailure.AlreadyVoted or AnoVetoFailure.NotActive)
        {
            Hide(context.PlayerId);
        }
    }
}
