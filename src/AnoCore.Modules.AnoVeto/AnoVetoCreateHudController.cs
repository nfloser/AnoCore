using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Maps;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.AnoVeto;

/// <summary>Separate per-player ANOMEME creation window. Voting remains server authoritative.</summary>
public sealed class AnoVetoCreateHudController : IDisposable
{
    public static readonly CustomHudId HudId = new("ano.veto.create");
    public const string LayoutResource = "panorama/layout/custom_game/anocore/ano_veto_create.xml";
    private static readonly ModuleId Owner = new("ano.veto");
    private const int PageSize = 8;
    private readonly ICustomHudService _hud;
    private readonly Func<PlayerId, CancellationToken, ValueTask<bool>> _startRandom;
    private readonly Func<PlayerId, IReadOnlyList<string>, CancellationToken, ValueTask<bool>> _startSelected;
    private readonly Func<IReadOnlyList<MapDefinition>> _getCatalog;
    private readonly IAnoVetoPresetRepository? _presets;
    private readonly Dictionary<PlayerId, PickerState> _states = [];
    private readonly object _gate = new();
    private readonly IDisposable _registration;
    private sealed class PickerState(AnoVetoCreateSession session)
    {
        public AnoVetoCreateSession Session { get; } = session;
        public int Page { get; set; }
        public IReadOnlyList<(AnoVetoPreset Preset, long Revision)> Saved { get; set; } = [];
        public string? SelectedSavedName { get; set; }
        public string? PendingDeleteName { get; set; }
        public Dictionary<int, string> ImageClasses { get; } = [];
    }

    public AnoVetoCreateHudController(
        ICustomHudService hud,
        Func<PlayerId, CancellationToken, ValueTask<bool>> startRandom,
        Func<IReadOnlyList<MapDefinition>> getCatalog,
        Func<PlayerId, IReadOnlyList<string>, CancellationToken, ValueTask<bool>> startSelected,
        IAnoVetoPresetRepository? presets = null)
    {
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
        _startRandom = startRandom ?? throw new ArgumentNullException(nameof(startRandom));
        _getCatalog = getCatalog ?? throw new ArgumentNullException(nameof(getCatalog));
        _startSelected = startSelected ?? throw new ArgumentNullException(nameof(startSelected));
        _presets = presets;
        var buttons = new List<string>
        {
            "ano_veto_create_close", "ano_veto_create_random", "ano_veto_create_manual",
            "ano_veto_create_saved", "ano_veto_create_back", "ano_veto_create_prev",
            "ano_veto_create_next", "ano_veto_create_confirm", "ano_veto_create_save",
            "ano_veto_create_duplicate", "ano_veto_create_delete"
        };
        buttons.AddRange(Enumerable.Range(0, PageSize).Select(i => $"ano_veto_create_map_{i}"));
        _registration = _hud.Register(Owner,
            new CustomHudDefinition(HudId, LayoutResource, "ano_veto_create_root",
                buttons, captureInput: true), OnClickAsync);
    }

    public bool Show(PlayerId player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (_gate)
        {
            ClearState(player);
            _hud.SetClass(player, HudId, "ano_veto_create_home", "inactive", false);
            _hud.SetClass(player, HudId, "ano_veto_create_picker", "active", false);
        }
        _hud.SetText(player, HudId, "ano_veto_create_status", "Choose how to build your map vote");
        return _hud.Show(player, HudId);
    }

    public void Hide(PlayerId player)
    {
        lock (_gate) ClearState(player);
        _hud.Hide(player, HudId);
    }

    private void ClearState(PlayerId player)
    {
        if (!_states.Remove(player, out var state)) return;
        foreach (var (index, preview) in state.ImageClasses)
            _hud.SetClass(player, HudId, $"ano_veto_create_map_{index}_image", preview, false);
        for (var i = 0; i < PageSize; i++)
            _hud.SetClass(player, HudId, $"ano_veto_create_map_{i}", "selected", false);
    }

    private void RenderPicker(PlayerId player, PickerState state, IReadOnlyList<MapDefinition> catalog)
    {
        var saved = state.Session.CurrentScreen == AnoVetoCreateSession.Screen.Saved;
        var maps = saved
            ? Array.Empty<(string MapId, string DisplayName)>()
            : state.Session.Browse(state.Page * PageSize, PageSize).ToArray();
        var savedPage = saved
            ? state.Saved.Skip(state.Page * PageSize).Take(PageSize).ToArray()
            : [];
        var mapLookup = catalog.ToDictionary(m => m.MapId, StringComparer.OrdinalIgnoreCase);
        var selectedMaps = state.Session.Draft.SelectedMapIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _hud.SetText(player, HudId, "ano_veto_create_progress",
            saved ? $"{state.Saved.Count} saved vetos · Page {state.Page + 1}"
                  : $"{selectedMaps.Count}/8 selected · Page {state.Page + 1}");
        _hud.SetText(player, HudId, "ano_veto_create_confirm_text",
            saved ? "START SAVED VETO" : "START SELECTED VETO");

        for (var i = 0; i < PageSize; i++)
        {
            var imagePanel = $"ano_veto_create_map_{i}_image";
            var card = $"ano_veto_create_map_{i}";
            if (state.ImageClasses.Remove(i, out var previous))
                _hud.SetClass(player, HudId, imagePanel, previous, false);

            var exists = saved ? i < savedPage.Length : i < maps.Length;
            if (!exists)
            {
                _hud.SetText(player, HudId, $"{card}_text", "");
                _hud.SetClass(player, HudId, card, "selected", false);
                continue;
            }

            string name;
            string? previewMapId;
            bool isSelected;
            if (saved)
            {
                var preset = savedPage[i].Preset;
                name = preset.Name;
                previewMapId = preset.MapIds[0];
                isSelected = string.Equals(state.SelectedSavedName, name, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                name = maps[i].DisplayName;
                previewMapId = maps[i].MapId;
                isSelected = selectedMaps.Contains(previewMapId);
            }

            _hud.SetText(player, HudId, $"{card}_text", name);
            _hud.SetClass(player, HudId, card, "selected", isSelected);
            if (mapLookup.TryGetValue(previewMapId, out var map))
            {
                var preview = AnoVetoMapPreview.GetClass(map);
                _hud.SetClass(player, HudId, imagePanel, preview, true);
                state.ImageClasses[i] = preview;
            }
        }
    }

    private async ValueTask OnClickAsync(CustomHudClickContext context)
    {
        if (context.HudId != HudId) return;
        var player = context.PlayerId;
        switch (context.ButtonId)
        {
            case "ano_veto_create_close":
                Hide(player);
                return;
            case "ano_veto_create_random":
                if (await _startRandom(player, context.CancellationToken).ConfigureAwait(false))
                    Hide(player);
                else
                    _hud.SetText(player, HudId, "ano_veto_create_status", "Unable to start vote. Check permission or active vote.");
                return;
            case "ano_veto_create_saved":
                _hud.SetText(player, HudId, "ano_veto_create_status", "Saved presets require persistent storage integration.");
                return;
            case "ano_veto_create_manual":
                {
                    var catalog = _getCatalog();
                    if (catalog.Count < AnoVetoMapDraft.RequiredMaps)
                    {
                        _hud.SetText(player, HudId, "ano_veto_create_status", "At least eight maps are required.");
                        return;
                    }
                    lock (_gate)
                    {
                        ClearState(player);
                        var state = new PickerState(new AnoVetoCreateSession(catalog.Select(m => (m.MapId, m.DisplayName))));
                        state.Session.OpenCreate();
                        _states[player] = state;
                        _hud.SetClass(player, HudId, "ano_veto_create_home", "inactive", true);
                        _hud.SetClass(player, HudId, "ano_veto_create_picker", "active", true);
                        RenderPicker(player, state, catalog);
                    }
                    return;
                }
        }

        if (context.ButtonId == "ano_veto_create_confirm")
        {
            IReadOnlyList<string>? selected = null;
            lock (_gate)
                if (_states.TryGetValue(player, out var state) && state.Session.Draft.CanConfirm)
                    selected = state.Session.Draft.Confirm();
            if (selected is null)
            {
                _hud.SetText(player, HudId, "ano_veto_create_status", "Choose exactly eight maps first.");
                return;
            }
            if (await _startSelected(player, selected, context.CancellationToken).ConfigureAwait(false))
                Hide(player);
            else
                _hud.SetText(player, HudId, "ano_veto_create_status", "Could not start selected veto.");
            return;
        }

        lock (_gate)
        {
            if (!_states.TryGetValue(player, out var state)) return;
            var catalog = _getCatalog();
            switch (context.ButtonId)
            {
                case "ano_veto_create_back":
                    ClearState(player);
                    _hud.SetClass(player, HudId, "ano_veto_create_home", "inactive", false);
                    _hud.SetClass(player, HudId, "ano_veto_create_picker", "active", false);
                    return;
                case "ano_veto_create_prev":
                    state.Page = Math.Max(0, state.Page - 1);
                    break;
                case "ano_veto_create_next":
                    if ((state.Page + 1) * PageSize < state.Session.Draft.AvailableMapCount)
                        state.Page++;
                    break;
                default:
                    const string prefix = "ano_veto_create_map_";
                    if (!context.ButtonId.StartsWith(prefix, StringComparison.Ordinal)
                        || !int.TryParse(context.ButtonId.AsSpan(prefix.Length), out var i)
                        || i is < 0 or >= PageSize)
                        return;
                    var rows = state.Session.Browse(state.Page * PageSize, PageSize);
                    if (i >= rows.Count || !state.Session.SelectMap(rows[i].MapId)) return;
                    break;
            }
            RenderPicker(player, state, catalog);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var player in _states.Keys.ToArray())
                ClearState(player);
        }
        _hud.HideAll(HudId);
        _registration.Dispose();
    }
}
