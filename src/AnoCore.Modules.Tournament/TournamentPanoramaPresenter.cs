using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Modules.Tournament;

/// <summary>
/// Projects event snapshots onto precompiled Panorama slots. Tournament data
/// never becomes Workshop resource markup and never requires an addon rebuild.
/// </summary>
public sealed class TournamentPanoramaPresenter : IDisposable
{
    public static readonly CustomHudId HudId = new("ano.tournament-view");
    public const string LayoutResource = "panorama/layout/custom_game/anocore/ano_tournament.xml";
    private static readonly string[] Views = ["overview", "table", "bracket", "matches", "teams"];
    private readonly ICustomHudService _hud;
    private readonly ITournamentEventRepository _events;
    private readonly IPlayerRegistry _players;
    private readonly object _gate = new();
    private readonly Dictionary<PlayerId, State> _open = [];
    private readonly List<IDisposable> _registrations = [];
    private bool _disposed;

    public TournamentPanoramaPresenter(ICustomHudService hud, ITournamentEventRepository events,
        IPlayerRegistry players, IAnoCommandRegistry commands, IAnoEventBus eventBus)
    {
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(eventBus);

        var buttons = Views.Select(view => "ano_tournament_tab_" + view)
            .Concat(["ano_tournament_close", "ano_tournament_prev", "ano_tournament_next"])
            .Concat(Enumerable.Range(0, 10).Select(i => $"ano_tournament_table_{i}"))
            .Concat(Enumerable.Range(0, 10).Select(i => $"ano_tournament_team_{i}"))
            .Concat(Enumerable.Range(0, 16).Select(i => $"ano_tournament_match_{i}"))
            .ToArray();
        try
        {
            _registrations.Add(hud.Register(new ModuleId("ano.tournament-view"),
                new CustomHudDefinition(HudId, LayoutResource, "ano_tournament_root",
                    buttons, captureInput: true), ClickAsync));
            _registrations.Add(commands.Register(new ModuleId("ano.tournament-view"),
                new CommandDescriptor("anotournament", "Open the shared tournament dashboard."),
                OpenCommandAsync));
            _registrations.Add(eventBus.Subscribe<PlayerDisconnectedEvent>((value, _) =>
            {
                Close(value.Player.Id);
                return ValueTask.CompletedTask;
            }));
            _registrations.Add(eventBus.Subscribe<PlayerReconnectedEvent>((value, _) =>
            {
                Close(value.Previous.Id);
                return ValueTask.CompletedTask;
            }));
        }
        catch
        {
            foreach (var registration in _registrations) registration.Dispose();
            throw;
        }
    }

    public async ValueTask RefreshOpenAsync(CancellationToken cancellationToken = default)
    {
        PlayerId[] ids;
        lock (_gate)
        {
            if (_disposed) return;
            ids = _open.Keys.ToArray();
        }
        if (ids.Length == 0) return;
        var active = await _events.LoadActiveAsync(cancellationToken).ConfigureAwait(false);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_disposed || !_open.TryGetValue(id, out var state)) continue;
                if (!IsCurrent(state)) { Close(id); continue; }
                Render(state, active);
            }
        }
    }

    private async ValueTask<CommandResult> OpenCommandAsync(CommandContext context)
    {
        if (context.Caller is null || !_players.TryGet(context.Caller, out var player)
            || player is not { IsConnected: true })
            return CommandResult.Fail(CommandFailureReason.Forbidden,
                "A connected CS2 player is required.");
        var active = await _events.LoadActiveAsync(context.CancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed || !IsCurrent(player))
                return CommandResult.Fail(CommandFailureReason.Forbidden, "Player disconnected.");
            if (!_open.TryGetValue(player.Id, out var state)
                || state.Player.SessionId != player.SessionId)
            {
                state = new State(player);
                _open[player.Id] = state;
            }
            Render(state, active, force: true);
            _hud.Show(player.Id, HudId);
        }
        return CommandResult.Ok("[ANO] Tournament overview opened.");
    }

    private async ValueTask ClickAsync(CustomHudClickContext context)
    {
        lock (_gate)
        {
            if (_disposed || context.HudId != HudId
                || !_open.TryGetValue(context.PlayerId, out var state) || !IsCurrent(state))
                return;
            if (context.ButtonId == "ano_tournament_close")
            {
                Close(context.PlayerId);
                return;
            }
            if (context.ButtonId.StartsWith("ano_tournament_tab_", StringComparison.Ordinal))
            {
                var view = context.ButtonId["ano_tournament_tab_".Length..];
                if (!Views.Contains(view, StringComparer.Ordinal)) return;
                state.View = view;
                state.Page = 0;
                state.Dirty = true;
            }
            else if (context.ButtonId == "ano_tournament_prev")
            {
                state.Page = Math.Max(0, state.Page - 1);
                state.Dirty = true;
            }
            else if (context.ButtonId == "ano_tournament_next")
            {
                state.Page++;
                state.Dirty = true;
            }
            else if (TryButtonIndex(context.ButtonId, "ano_tournament_team_", 10, out var index))
            {
                state.SelectedTeamRow = index;
                state.SelectedTableRow = null;
                state.Dirty = true;
            }
            else if (TryButtonIndex(context.ButtonId, "ano_tournament_table_", 10, out index))
            {
                state.SelectedTableRow = index;
                state.SelectedTeamRow = null;
                state.Dirty = true;
            }
            else if (TryButtonIndex(context.ButtonId, "ano_tournament_match_", 16, out index))
            {
                state.SelectedMatchRow = index;
                state.Dirty = true;
            }
            else return;
        }
        await RefreshOpenAsync(context.CancellationToken).ConfigureAwait(false);
    }

    private void Render(State state, TournamentStoredEvent? eventState, bool force = false)
    {
        var revision = eventState?.Revision ?? 0;
        if (!force && !state.Dirty && state.RenderedRevision == revision) return;
        var player = state.Player.Id;
        var definition = eventState?.Definition;
        var groups = definition is null
            ? []
            : definition.Groups.Count == 0
                ? [new TournamentGroupDefinition { Id = "main", Name = "Liga",
                    TeamIds = definition.Teams.Select(team => team.Id).ToList() }]
                : definition.Groups;

        var tablePages = Math.Max(1, groups.Count);
        var fixtures = definition is null ? [] : groups.SelectMany(group =>
            TournamentRoundRobin.Generate(definition, group.Id).SelectMany(round => round.Fixtures)).ToArray();
        var fixturePages = Math.Max(1, (fixtures.Length + 15) / 16);
        var bracket = definition is null ? null : TournamentSingleElimination.Generate(
            definition.Teams.OrderBy(team => team.InitialSeed ?? int.MaxValue)
                .ThenBy(team => team.Id, StringComparer.Ordinal)
                .Select((team, index) => new TournamentBracketEntrant(team.Id, index + 1)).ToArray());
        var bracketMatches = bracket?.Rounds.SelectMany(round => round.Matches).ToArray() ?? [];
        var pageCount = state.View switch
        {
            "table" => tablePages,
            "matches" => fixturePages,
            "bracket" => Math.Max(1, (bracketMatches.Length + 15) / 16),
            _ => 1,
        };
        state.Page = Math.Clamp(state.Page, 0, pageCount - 1);
        Set(player, "ano_tournament_title", definition?.Name ?? "AnoTournament");
        Set(player, "ano_tournament_status", definition is null
            ? "No active tournament · ask an organizer to import a tournament."
            : $"{definition.Teams.Count} Teams · {eventState!.ConfirmedResults.Count} Ergebnisse · Revision {revision}");
        Set(player, "ano_tournament_heading", state.View.ToUpperInvariant());
        Set(player, "ano_tournament_description", definition is null
            ? "Waiting for server tournament configuration."
            : state.View switch
            {
                "table" => groups[state.Page].Name,
                "bracket" => "Gesetzter K.-o.-Baum (Planung, Ergebnisse folgen)",
                "matches" => "Gruppenspiele und bestätigte Ergebnisse",
                "teams" => "Teams und registrierte Spieler",
                _ => "Turnierübersicht",
            });
        Set(player, "ano_tournament_page", $"{state.Page + 1} / {pageCount}");
        Set(player, "ano_tournament_sync", definition is null ? "Noch kein Turnier" : $"Datenbank · Rev {revision}");
        _hud.SetClass(player, HudId, "ano_tournament_prev", "disabled", state.Page == 0);
        _hud.SetClass(player, HudId, "ano_tournament_next", "disabled", state.Page + 1 == pageCount);
        foreach (var view in Views)
            _hud.SetClass(player, HudId, "ano_tournament_root", "view-" + view, view == state.View);

        var summaries = definition is null
            ? new[] { "Kein Turnier aktiv", "0 Teams", "0 Spiele", "0 Ergebnisse" }
            : new[] { $"{definition.Teams.Count} Teams", $"{groups.Count} Gruppen",
                $"{fixtures.Length} Gruppenspiele", $"{eventState!.ConfirmedResults.Count} Ergebnisse" };
        for (var i = 0; i < 4; i++)
            Set(player, $"ano_tournament_summary_{i}", summaries[i]);

        var group = groups.Count == 0 ? null : groups[Math.Min(state.Page, groups.Count - 1)];
        var results = eventState?.ConfirmedResults ?? [];
        var groupResults = group is null ? [] : results.Where(result =>
            result.FixtureId.StartsWith(group.Id + ":", StringComparison.Ordinal)).ToArray();
        var standings = definition is null || group is null
            ? []
            : TournamentStandings.Calculate(definition, group.Id, groupResults).ToArray();
        var names = definition?.Teams.ToDictionary(team => team.Id, team => team.Name, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < 10; i++)
        {
            var show = i < standings.Length;
            _hud.SetClass(player, HudId, $"ano_tournament_table_{i}", "hidden", !show);
            Set(player, $"ano_tournament_table_{i}_name",
                show ? $"{i + 1}. {names[standings[i].TeamId]}" : string.Empty);
            Set(player, $"ano_tournament_table_{i}_stats", show
                ? $"{standings[i].GamesPlayed} | {standings[i].Wins} | {standings[i].Draws} | " +
                    $"{standings[i].Losses} | {standings[i].Points} | " +
                    $"{standings[i].MapDifference:+#;-#;0} | {standings[i].RoundDifference:+#;-#;0}"
                : string.Empty);
        }

        for (var i = 0; i < 16; i++)
        {
            var index = state.Page * 16 + i;
            var show = index < bracketMatches.Length;
            _hud.SetClass(player, HudId, $"ano_tournament_match_{i}", "hidden", !show);
            Set(player, $"ano_tournament_match_{i}_text", show
                ? $"R{bracketMatches[index].Round} · " +
                    $"{Source(bracketMatches[index].Home, names)} vs {Source(bracketMatches[index].Away, names)}"
                : string.Empty);
            var fixtureShow = index < fixtures.Length;
            if (fixtureShow)
            {
                var fixture = fixtures[index];
                var result = results.FirstOrDefault(value => value.FixtureId == fixture.Id);
                Set(player, $"ano_tournament_fixture_{i}", $"{names[fixture.HomeTeamId]} vs " +
                    $"{names[fixture.AwayTeamId]} · " +
                    (result is null ? "ausstehend" : $"{result.HomeMaps}:{result.AwayMaps}"));
            }
            else Set(player, $"ano_tournament_fixture_{i}", string.Empty);
        }

        for (var i = 0; i < 10; i++)
        {
            var visible = definition is not null && i < definition.Teams.Count;
            _hud.SetClass(player, HudId, $"ano_tournament_team_{i}", "hidden", !visible);
            Set(player, $"ano_tournament_team_{i}_name",
                visible ? definition!.Teams[i].Name : string.Empty);
        }

        TournamentEventTeamDefinition? selected = null;
        if (definition is not null)
        {
            if (state.SelectedTeamRow is { } teamIndex && teamIndex < definition.Teams.Count)
                selected = definition.Teams[teamIndex];
            else if (state.SelectedTableRow is { } tableIndex && tableIndex < standings.Length)
                selected = definition.Teams.Single(team => team.Id == standings[tableIndex].TeamId);
            selected ??= definition.Teams.First();
        }
        Set(player, "ano_tournament_team_name", selected?.Name ?? "Kein Team ausgewählt");
        Set(player, "ano_tournament_team_status", selected is null
            ? "Warte auf Anmeldung"
            : $"{selected.Tag} · {selected.Starters.Count} Starter · " +
                $"{selected.Substitutes.Count} Ersatz · Präsenz nur lokaler Server");
        var roster = selected is null
            ? []
            : selected.Starters.Concat(selected.Substitutes).Take(7).ToArray();
        for (var i = 0; i < 7; i++)
        {
            var steam = i < roster.Length ? roster[i] : 0UL;
            var online = steam != 0 && _players.TryGet(new PlayerId(steam), out var other)
                && other is { IsConnected: true };
            Set(player, $"ano_tournament_roster_{i}", steam == 0 ? "" :
                $"{(i < 5 ? "Starter" : "Ersatz")} {i + 1}: {steam} " +
                (online ? "· Online hier" : "· Nicht auf diesem Server"));
        }
        state.RenderedRevision = revision;
        state.Dirty = false;
    }

    private static string Source(TournamentBracketSlot slot, IReadOnlyDictionary<string, string> names)
        => slot.TeamId is { } id ? names.GetValueOrDefault(id, id)
            : slot.FromMatchId is { } match ? "Sieger " + match : "Freilos";

    private void Set(PlayerId player, string id, string? value)
    {
        var text = new string((value ?? "").Where(c => !char.IsControl(c)).Take(160).ToArray());
        _hud.SetText(player, HudId, id, text);
    }

    private static bool TryButtonIndex(string input, string prefix, int limit, out int index)
    {
        index = -1;
        return input.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(input.AsSpan(prefix.Length), out index)
            && index >= 0 && index < limit;
    }

    private bool IsCurrent(State state) => IsCurrent(state.Player);
    private bool IsCurrent(PlayerSnapshot player)
        => _players.TryGet(player.Id, out var current) && current is { IsConnected: true }
            && current.SessionId == player.SessionId;

    private void Close(PlayerId id)
    {
        lock (_gate)
        {
            _open.Remove(id);
            _hud.Hide(id, HudId);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _open.Clear();
            _hud.HideAll(HudId);
            foreach (var registration in _registrations) registration.Dispose();
        }
    }

    private sealed class State(PlayerSnapshot player)
    {
        public PlayerSnapshot Player { get; } = player;
        public string View { get; set; } = "overview";
        public int Page { get; set; }
        public int? SelectedTeamRow { get; set; }
        public int? SelectedTableRow { get; set; }
        public int? SelectedMatchRow { get; set; }
        public long RenderedRevision { get; set; } = -1;
        public bool Dirty { get; set; } = true;
    }
}
