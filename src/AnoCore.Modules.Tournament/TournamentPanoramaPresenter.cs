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
    private readonly ITournamentPresenceRepository? _presence;
    private readonly ITournamentMatchAssignmentRepository? _assignments;
    private readonly bool _matchZyReadyOwned;
    private long _readSequence;
    private readonly object _gate = new();
    private readonly Dictionary<PlayerId, State> _open = [];
    private readonly List<IDisposable> _registrations = [];
    private bool _disposed;

    public TournamentPanoramaPresenter(ICustomHudService hud, ITournamentEventRepository events,
        IPlayerRegistry players, IAnoCommandRegistry commands, IAnoEventBus eventBus, ITournamentPresenceRepository? presence = null, ITournamentMatchAssignmentRepository? assignments = null, bool matchZyReadyOwned = false)
    {
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _presence = presence;
        _assignments = assignments;
        _matchZyReadyOwned = matchZyReadyOwned;
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
        var readSequence = Interlocked.Increment(ref _readSequence);
        var active = await _events.LoadActiveAsync(cancellationToken).ConfigureAwait(false);
        var presence = _presence is null ? null : await _presence.LoadOnlineAsync(cancellationToken).ConfigureAwait(false);
        var assignments = _assignments is null ? [] : await _assignments.LoadOpenAsync(cancellationToken).ConfigureAwait(false);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_disposed || !_open.TryGetValue(id, out var state)) continue;
                if (!IsCurrent(state)) { Close(id); continue; }
                Render(state, active, presence, assignments, readSequence);
            }
        }
    }

    private async ValueTask<CommandResult> OpenCommandAsync(CommandContext context)
    {
        if (context.Caller is null || !_players.TryGet(context.Caller, out var player)
            || player is not { IsConnected: true })
            return CommandResult.Fail(CommandFailureReason.Forbidden,
                "A connected CS2 player is required.");
        var readSequence = Interlocked.Increment(ref _readSequence);
        var active = await _events.LoadActiveAsync(context.CancellationToken).ConfigureAwait(false);
        var presence = _presence is null ? null : await _presence.LoadOnlineAsync(context.CancellationToken).ConfigureAwait(false);
        var assignments = _assignments is null ? [] : await _assignments.LoadOpenAsync(context.CancellationToken).ConfigureAwait(false);
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
            Render(state, active, presence, assignments, readSequence, force: true);
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
                if (state.View != "teams" || index >= state.TeamRowIds.Length) return;
                state.SelectedTeamId = state.TeamRowIds[index];
                state.Page = 0;
                state.Dirty = true;
            }
            else if (TryButtonIndex(context.ButtonId, "ano_tournament_table_", 10, out index))
            {
                if (state.View != "table" || index >= state.TableRowIds.Length) return;
                state.SelectedTeamId = state.TableRowIds[index];
                state.Dirty = true;
            }
            else if (TryButtonIndex(context.ButtonId, "ano_tournament_match_", 16, out index))
            {
                if (state.View != "bracket" || index >= state.MatchRowIds.Length) return;
                state.SelectedMatchId = state.MatchRowIds[index];
                state.Dirty = true;
            }
            else return;
        }
        await RefreshOpenAsync(context.CancellationToken).ConfigureAwait(false);
    }

    private void Render(State state, TournamentStoredEvent? eventState,
        IReadOnlyList<TournamentServerPresence>? presence, IReadOnlyList<TournamentMatchAssignment> assignments, long readSequence, bool force = false)
    {
        if (readSequence < state.RenderedReadSequence) return;
        state.RenderedReadSequence = readSequence;
        var presenceKey = presence is null ? "local" : string.Join("|", presence.OrderBy(server => server.ServerId, StringComparer.Ordinal)
            .SelectMany(server => server.Players.OrderBy(p => p.SteamId).Select(p =>
                $"{server.ServerId}:{server.InstanceId}:{p.SteamId}:{p.SessionId}:{p.Name}:{p.MatchId}:{p.IsReady}")));
        presenceKey += "|" + string.Join("|", assignments.OrderBy(match => match.Id).Select(match => $"{match.Id}:{match.EventId}:{match.FixtureId}:{match.ServerId}:{match.State}"));
        var revision = eventState?.Revision ?? 0;
        var eventId = eventState?.Definition.Id;
        if (string.Equals(state.RenderedEventId, eventId, StringComparison.Ordinal)
            && revision < state.RenderedRevision) return;
        if (!force && !state.Dirty && state.RenderedRevision == revision
            && string.Equals(state.RenderedEventId, eventId, StringComparison.Ordinal)
            && state.RenderedPresenceKey == presenceKey) return;
        if (state.RenderedEventId is not null
            && !string.Equals(state.RenderedEventId, eventId, StringComparison.Ordinal))
        {
            state.View = "overview";
            state.Page = 0;
            state.SelectedTeamId = null;
            state.SelectedMatchId = null;
        }
        var player = state.Player.Id;
        var definition = eventState?.Definition;
        var groups = definition is null || !definition.GroupStageEnabled
            ? []
            : definition.Groups.Count == 0
                ? [new TournamentGroupDefinition { Id = "main", Name = "Liga",
                    TeamIds = definition.Teams.Select(team => team.Id).ToList() }]
                : definition.Groups;

        var tablePages = Math.Max(1, groups.Count);
        var fixtures = definition is null ? [] : groups.SelectMany(group =>
            TournamentSchedules.Generate(definition, group.Id, eventState!.ConfirmedResults.Where(result => result.FixtureId.StartsWith(group.Id + ":", StringComparison.Ordinal)).ToArray()).SelectMany(round => round.Fixtures)).ToArray();
        var fixturePages = Math.Max(1, (fixtures.Length + 15) / 16);
        var entrants = definition is null ? null : TournamentEventPlayoffs.ResolveEntrants(definition, eventState!.ConfirmedResults);
        var bracket = entrants is null ? null : TournamentPlayoffPlans.Generate(definition!, entrants);
        var progress = definition is null ? null : TournamentEventPlayoffs.Progress(definition, eventState!.ConfirmedResults, eventState.PlayoffResults);
        var winners = progress?.Matches.Where(match => match.WinnerTeamId is not null)
            .ToDictionary(match => match.Id, match => match.WinnerTeamId!, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var losers = progress?.Matches.Where(match => match.WinnerTeamId is not null).ToDictionary(match => match.Id,
            match => match.WinnerTeamId == match.HomeTeamId ? match.AwayTeamId! : match.HomeTeamId!, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var bracketMatches = bracket?.Rounds.SelectMany(round => round.Matches).ToArray() ?? [];
        var eventAssignments = assignments.Where(match => match.EventId == definition?.Id).ToArray();
        string AssignmentStatus(string id) => eventAssignments.FirstOrDefault(match => match.FixtureId == id) is { } match
            ? $" · {match.ServerId} [{match.State}]" : "";
        var selectedRoster = definition?.Teams.FirstOrDefault(team => team.Id == state.SelectedTeamId)
            ?? definition?.Teams.FirstOrDefault();
        var rosterPages = Math.Max(1, ((selectedRoster?.Starters.Count ?? 0) + (selectedRoster?.Substitutes.Count ?? 0) + 6) / 7);
        var pageCount = state.View switch
        {
            "table" => tablePages,
            "teams" => rosterPages,
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
                "table" => groups.Count == 0 ? "Keine Gruppenphase · direktes Playoff" : groups[state.Page].Name,
                "bracket" => !definition.PlayoffsEnabled ? "Playoffs deaktiviert" : entrants is null ? "Playoffs warten auf alle Gruppenergebnisse"
                    : definition.PlayoffFormat == TournamentPlayoffFormat.DoubleElimination ? "Double Elimination · Upper / Lower / Finale" : "K.-o.-Baum · bestätigte Sieger und nächste Paarungen",
                "matches" => definition.GroupFormat + " · Gruppenspiele und bestätigte Ergebnisse",
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
        if (progress?.ChampionTeamId is { } champion)
            summaries[3] = "Turniersieger: " + definition!.Teams.Single(team => team.Id == champion).Name;
        for (var i = 0; i < 4; i++)
            Set(player, $"ano_tournament_summary_{i}", summaries[i]);

        var group = groups.Count == 0 ? null : groups[Math.Min(state.Page, groups.Count - 1)];
        var results = eventState?.ConfirmedResults ?? [];
        var groupResults = group is null ? [] : results.Where(result =>
            result.FixtureId.StartsWith(group.Id + ":", StringComparison.Ordinal)).ToArray();
        var standings = definition is null || group is null || !definition.GroupStageEnabled
            ? []
            : TournamentStandings.Calculate(definition, group.Id, groupResults).ToArray();
        state.TableRowIds = standings.Select(row => row.TeamId).ToArray();
        state.TeamRowIds = definition?.Teams.Select(team => team.Id).ToArray() ?? [];
        state.MatchRowIds = bracketMatches.Skip(state.Page * 16).Take(16).Select(match => match.Id).ToArray();
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
            _hud.SetClass(player, HudId, $"ano_tournament_match_{i}", "disabled", show && !progress!.Matches.Single(match => match.Id == bracketMatches[index].Id).IsEnabled);
            Set(player, $"ano_tournament_match_{i}_text", show
                ? BracketRound(bracketMatches[index]) + " · " +
                    $"{Source(bracketMatches[index].Home, names, winners, losers)} vs {Source(bracketMatches[index].Away, names, winners, losers)}" +
                    MatchStatus(bracketMatches[index].Id, progress!, eventState!.PlayoffResults, names) + AssignmentStatus(bracketMatches[index].Id)
                : string.Empty);
            var fixtureShow = index < fixtures.Length;
            if (fixtureShow)
            {
                var fixture = fixtures[index];
                var result = results.FirstOrDefault(value => value.FixtureId == fixture.Id);
                Set(player, $"ano_tournament_fixture_{i}", $"{names[fixture.HomeTeamId]} vs " +
                    $"{names[fixture.AwayTeamId]} · " +
                    (result is null ? "ausstehend" : $"{result.HomeMaps}:{result.AwayMaps}") + AssignmentStatus(fixture.Id));
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

        var selected = selectedRoster;
        Set(player, "ano_tournament_team_name", selected?.Name ?? "Kein Team ausgewählt");
        var missing = selected is null ? 0 : selected.Starters.Count(steam => presence is null
            ? !_players.TryGet(new PlayerId(steam), out var local) || local is not { IsConnected: true }
            : !presence.Any(server => server.Players.Any(p => p.SteamId == steam)));
        var readyCount = selected is null || presence is null ? 0 : selected.Starters.Count(steam =>
        {
            var matches = presence.SelectMany(server => server.Players.Where(p => p.SteamId == steam)).ToArray();
            return matches.Length == 1 && matches[0].IsReady;
        });
        var matchZyReady = _matchZyReadyOwned || eventAssignments.Any(match => match.HomeTeamId == selected?.Id || match.AwayTeamId == selected?.Id);
        Set(player, "ano_tournament_team_status", selected is null ? "Warte auf Anmeldung"
            : $"{selected.Tag} · Online {selected.Starters.Count - missing}/{selected.Starters.Count} · " +
                (matchZyReady ? "Ready: MatchZy" : presence is null ? "Präsenz nur lokal" : $"Bereit {readyCount}/{selected.Starters.Count}") +
                $" · Fehlen {missing} · {selected.Substitutes.Count} Ersatz");
        var roster = selected is null
            ? []
            : selected.Starters.Concat(selected.Substitutes).Skip(state.View == "teams" ? state.Page * 7 : 0).Take(7).ToArray();
        for (var i = 0; i < 7; i++)
        {
            var rosterIndex = (state.View == "teams" ? state.Page * 7 : 0) + i;
            var steam = i < roster.Length ? roster[i] : 0UL;
            var online = steam != 0 && _players.TryGet(new PlayerId(steam), out var other)
                && other is { IsConnected: true };
            var remote = presence?.SelectMany(server => server.Players.Where(p => p.SteamId == steam)
                .Select(p => (server.ServerId, Player: p))).ToArray();
            var status = remote is null ? online ? "Online hier" : "Nicht auf diesem Server"
                : remote.Length == 0 ? "Offline" : remote.Length > 1 ? "Online · mehrere Server/Sessions"
                : $"{new string(remote[0].Player.Name.Take(32).ToArray())} · Online auf {remote[0].ServerId} · " +
                    (matchZyReady ? "Ready: MatchZy" : remote[0].Player.IsReady ? "Bereit für " + remote[0].Player.MatchId?.ToString("N")[..8] : "Nicht bereit");
            Set(player, $"ano_tournament_roster_{i}", steam == 0 ? "" :
                $"{(rosterIndex < 5 ? "Starter" : "Ersatz")} {(rosterIndex < 5 ? rosterIndex + 1 : rosterIndex - 4)}: {steam} · {status}");
        }
        if (state.View == "bracket" && state.SelectedMatchId is { } selectedMatch)
        {
            var match = bracketMatches.FirstOrDefault(value => value.Id == selectedMatch);
            if (match is not null)
            {
                Set(player, "ano_tournament_team_name", "Match " + match.Id);
                Set(player, "ano_tournament_team_status",
                    Source(match.Home, names, winners, losers) + " vs " + Source(match.Away, names, winners, losers) +
                    MatchStatus(match.Id, progress!, eventState!.PlayoffResults, names));
                Set(player, "ano_tournament_roster_0", "K.-o.-Runde " + match.Round);
                for (var i = 1; i < 7; i++) Set(player, $"ano_tournament_roster_{i}", string.Empty);
            }
        }
        state.RenderedPresenceKey = presenceKey;
        state.RenderedEventId = eventId;
        state.RenderedRevision = revision;
        state.Dirty = false;
    }

    private static string BracketRound(TournamentBracketMatch match) => match.Bracket switch
    {
        "lower" => "Lower R" + match.Round,
        "final" => "Finale",
        "final-reset" => "Reset",
        _ => "Upper R" + match.Round,
    };

    private static string Source(TournamentBracketSlot slot, IReadOnlyDictionary<string, string> names, IReadOnlyDictionary<string, string> winners, IReadOnlyDictionary<string, string> losers)
        => slot.TeamId is { } id ? names.GetValueOrDefault(id, id)
            : slot.FromMatchId is { } match
                ? (slot.FromLoser ? losers : winners).TryGetValue(match, out var team) ? names.GetValueOrDefault(team, team) : (slot.FromLoser ? "Verlierer " : "Sieger ") + match
                : "Freilos";

    private static string MatchStatus(string id, TournamentBracketProgressSnapshot progress,
        IReadOnlyList<TournamentBracketResult> results, IReadOnlyDictionary<string, string> names)
    {
        var match = progress.Matches.Single(value => value.Id == id);
        var result = results.FirstOrDefault(value => value.MatchId == id);
        if (!match.IsEnabled) return progress.ChampionTeamId is null ? " · noch nicht aktiviert" : " · nicht erforderlich";
        return result is not null ? $" · Sieger {names.GetValueOrDefault(result.WinnerTeamId, result.WinnerTeamId)} {result.WinnerMaps}:{result.LoserMaps}"
            : match.IsReady ? " · bereit" : " · wartet";
    }

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
        public string? SelectedTeamId { get; set; }
        public string? SelectedMatchId { get; set; }
        public string[] TeamRowIds { get; set; } = [];
        public string[] TableRowIds { get; set; } = [];
        public string[] MatchRowIds { get; set; } = [];
        public long RenderedReadSequence { get; set; }
        public string? RenderedPresenceKey { get; set; }
        public string? RenderedEventId { get; set; }
        public long RenderedRevision { get; set; } = -1;
        public bool Dirty { get; set; } = true;
    }
}
