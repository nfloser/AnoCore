using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.AnoVeto;

public sealed class AnoVetoHudController : IDisposable
{
    private static readonly ModuleId Owner = new("ano.veto");
    public static readonly CustomHudId HudId = new("ano.veto");
    public const string LayoutResource = "panorama/layout/custom_game/anocore/ano_veto_cards.xml";

    private const string RootPanelId = "ano_veto_root";
    private const string ConfirmButtonId = "ano_veto_confirm";
    private const string CloseButtonId = "ano_veto_close";
    private const string MapButtonPrefix = "ano_veto_map_";
    private readonly ICustomHudService _hud;
    private readonly AnoVetoCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;
    private readonly Func<PlayerId, string> _playerName;
    private readonly HashSet<PlayerId> _viewers = [];
    private readonly object _previewGate = new();
    private readonly Dictionary<(PlayerId Player, int Index), string> _previewClasses = [];
    private IDisposable? _registration;

    public AnoVetoHudController(
        ICustomHudService hud,
        AnoVetoCoordinator coordinator,
        TimeProvider? timeProvider = null,
        Func<PlayerId, string>? playerName = null)
    {
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _playerName = playerName ?? (player => player.SteamId64.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var buttonIds = Enumerable.Range(0, 8)
            .Select(index => $"{MapButtonPrefix}{index}")
            .Append(CloseButtonId)
            .Append(ConfirmButtonId)
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

        if (!_coordinator.TryGetDraft(playerId, out var selectedMaps, out var maxVotes, out var confirmed))
        {
            return false;
        }

        var selectedIds = selectedMaps.Select(map => map.MapId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _hud.SetText(playerId, HudId, "ano_veto_title", "Choose the next map");
        _hud.SetText(playerId, HudId, "ano_veto_status",
            confirmed
                ? $"Abgestimmt: {selectedMaps.Count} Maps bestätigt"
                : $"1–{maxVotes} Maps wählen und bestätigen ({selectedMaps.Count}/{maxVotes} gewählt)");

        _hud.SetText(playerId, HudId, "ano_veto_confirm_text", confirmed ? "Abgestimmt" : "Auswahl bestätigen");
        _hud.SetClass(playerId, HudId, ConfirmButtonId, "unavailable", confirmed || selectedMaps.Count == 0);
        if (_coordinator.TryGetBallotStatus(out var voters, out var eligible))
        {
            var names = voters.Select(id => new string(_playerName(id).Where(c => !char.IsControl(c)).Take(48).ToArray()));
            _hud.SetText(playerId, HudId, "ano_veto_voters", $"Abgestimmt ({voters.Count}/{eligible}): "
                + (voters.Count == 0 ? "Noch niemand" : string.Join(", ", names)));
        }
        lock (_previewGate) _viewers.Add(playerId);

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
            _hud.SetClass(playerId, HudId, $"ano_veto_map_{index}", "selected",
                selectedIds.Contains(maps[index].MapId));
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
        lock (_previewGate) _viewers.Remove(playerId);
        return hidden;
    }

    public void HideAll()
    {
        _hud.HideAll(HudId);
        ClearPreviews(null);
        lock (_previewGate) _viewers.Clear();
    }

    private void ClearPreviews(PlayerId? playerId)
    {
        lock (_previewGate)
        {
            foreach (var pair in _previewClasses.Where(pair => playerId is null || pair.Key.Player == playerId).ToArray())
            {
                _hud.SetClass(pair.Key.Player, HudId, $"ano_veto_map_{pair.Key.Index}_image", pair.Value, false);
                _hud.SetClass(pair.Key.Player, HudId, $"ano_veto_map_{pair.Key.Index}", "selected", false);
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

        AnoVetoOperationResult result;
        if (string.Equals(context.ButtonId, ConfirmButtonId, StringComparison.Ordinal))
        {
            result = await _coordinator.ConfirmSelectionsAsync(context.PlayerId,
                _timeProvider.GetUtcNow(), context.CancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (!context.ButtonId.StartsWith(MapButtonPrefix, StringComparison.Ordinal)
                || !int.TryParse(context.ButtonId.AsSpan(MapButtonPrefix.Length), out var index)
                || index is < 0 or >= 8 || !_coordinator.TryGetStatus(out var maps)
                || index >= maps.Count) return;
            result = _coordinator.ToggleSelection(context.PlayerId, maps[index].MapId, _timeProvider.GetUtcNow());
        }

        if (result.Outcome != AnoVetoOutcome.None)
        {
            HideAll();
            return;
        }
        if (result.Failure == AnoVetoFailure.NotActive)
        {
            Hide(context.PlayerId);
            return;
        }
        if (result.Accepted && string.Equals(context.ButtonId, ConfirmButtonId, StringComparison.Ordinal))
            Hide(context.PlayerId);
        PlayerId[] viewers;
        lock (_previewGate) viewers = _viewers.ToArray();
        foreach (var viewer in viewers) Show(viewer);
        if (!result.Accepted)
            _hud.SetText(context.PlayerId, HudId, "ano_veto_status", result.Failure switch
            {
                AnoVetoFailure.AlreadyVoted => "Deine Stimme ist bereits bestätigt.",
                AnoVetoFailure.InvalidMap => "Wähle mindestens eine Map vor dem Bestätigen.",
                AnoVetoFailure.NotEligible => "Du bist bei dieser Abstimmung nicht stimmberechtigt.",
                _ => "Maximal drei Maps. Klicke eine gewählte Map erneut zum Abwählen.",
            });
    }
}
