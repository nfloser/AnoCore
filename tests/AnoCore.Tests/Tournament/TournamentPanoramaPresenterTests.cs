using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Tournament;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Tests.AnoVeto;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentPanoramaPresenterTests
{
    private static readonly PlayerId Viewer = new(76561198000911001);

    [TestMethod]
    public async Task OpenAndSwitchViews_UsesStaticSlotsAndShowsCurrentGroupResults()
    {
        var bus = new AnoEventBus();
        var players = new PlayerRegistry(bus);
        await players.ConnectAsync(new(Viewer, "Viewer", PlayerTeam.Terrorist,
            true, DateTimeOffset.UtcNow));
        var repository = new MemoryEvents(Definition());
        var hud = new TestCustomHudService();
        var commands = new CommandRegistry(new Allow());
        using var presenter = new TournamentPanoramaPresenter(
            hud, repository, players, commands, bus);

        var result = await commands.ExecuteAsync("!anotournament", Viewer);
        Assert.IsTrue(result.Success);
        Assert.IsTrue(hud.VisiblePlayers(TournamentPanoramaPresenter.HudId).Contains(Viewer));
        Assert.IsTrue(hud.Definition(TournamentPanoramaPresenter.HudId)!.CaptureInput);
        Assert.AreEqual("3 Teams", hud.Text(Viewer,
            TournamentPanoramaPresenter.HudId, "ano_tournament_summary_0"));

        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_tab_table");
        Assert.IsTrue(hud.Class(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_root", "view-table"));
        Assert.IsTrue(hud.Text(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_table_0_name")!.Contains("Squad", StringComparison.Ordinal));

        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_tab_bracket");
        Assert.IsTrue(hud.Text(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_match_0_text")!.Contains("vs", StringComparison.Ordinal));
        Assert.IsTrue(hud.Class(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_match_4", "hidden"));
        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_match_0");
        Assert.IsTrue(hud.Text(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_team_name")!.StartsWith("Match ", StringComparison.Ordinal));

        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_tab_teams");
        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_team_1");
        Assert.AreEqual("Squad 2", hud.Text(Viewer,
            TournamentPanoramaPresenter.HudId, "ano_tournament_team_name"));
        Assert.IsTrue(hud.Text(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_roster_0")!.Contains("Starter", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ChangedRevisionRefreshesExistingHudAndCloseClearsCursor()
    {
        var bus = new AnoEventBus();
        var players = new PlayerRegistry(bus);
        await players.ConnectAsync(new(Viewer, "Viewer", PlayerTeam.Terrorist,
            true, DateTimeOffset.UtcNow));
        var repository = new MemoryEvents(Definition());
        var hud = new TestCustomHudService();
        var commands = new CommandRegistry(new Allow());
        using var presenter = new TournamentPanoramaPresenter(
            hud, repository, players, commands, bus);

        Assert.IsTrue((await commands.ExecuteAsync("!anotournament", Viewer)).Success);
        Assert.AreEqual("1 / 1", hud.Text(Viewer,
            TournamentPanoramaPresenter.HudId, "ano_tournament_page"));
        repository.Revision = 2;
        repository.Definition.Name = "Renamed Cup";
        await presenter.RefreshOpenAsync();
        Assert.AreEqual("Renamed Cup", hud.Text(Viewer,
            TournamentPanoramaPresenter.HudId, "ano_tournament_title"));
        repository.Revision = 1;
        repository.Definition.Name = "Stale database read";
        await presenter.RefreshOpenAsync();
        Assert.AreEqual("Renamed Cup", hud.Text(Viewer,
            TournamentPanoramaPresenter.HudId, "ano_tournament_title"));
        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_close");
        Assert.AreEqual(0, hud.VisiblePlayers(TournamentPanoramaPresenter.HudId).Count);
    }

    [TestMethod]
    public async Task PersistedPlayoffWinnerUpdatesFinalAndChampionWithoutChangingResources()
    {
        var bus = new AnoEventBus();
        var players = new PlayerRegistry(bus);
        await players.ConnectAsync(new(Viewer, "Viewer", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        var repository = new MemoryEvents(Definition());
        var hud = new TestCustomHudService();
        var commands = new CommandRegistry(new Allow());
        using var presenter = new TournamentPanoramaPresenter(hud, repository, players, commands, bus);
        await commands.ExecuteAsync("!anotournament", Viewer);
        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_tab_bracket");
        repository.PlayoffResults = [new("r1-m1", "team-2", 1, 0)];
        repository.Revision++;
        await presenter.RefreshOpenAsync();
        var final = hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_match_1_text")!;
        StringAssert.Contains(final, "Squad 1 vs Squad 2");
        StringAssert.Contains(final, "bereit");
        repository.PlayoffResults = [.. repository.PlayoffResults, new("r2-m1", "team-1", 2, 1)];
        repository.Revision++;
        await presenter.RefreshOpenAsync();
        StringAssert.Contains(hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_summary_3")!, "Squad 1");
    }

    [TestMethod]
    public async Task RemotePresenceAndReadyRefreshWithoutAnEventRevisionChange()
    {
        var bus = new AnoEventBus();
        var players = new PlayerRegistry(bus);
        await players.ConnectAsync(new(Viewer, "Viewer", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        var repository = new MemoryEvents(Definition());
        var remotePlayer = new TournamentPlayerPresence(repository.Definition.Teams[0].Starters[0], Guid.NewGuid(), "Remote", Guid.NewGuid(), false);
        var presence = new Presence { States = [new("server-b", Guid.NewGuid(), [remotePlayer], DateTimeOffset.UtcNow.AddSeconds(30))] };
        var hud = new TestCustomHudService();
        var commands = new CommandRegistry(new Allow());
        using var presenter = new TournamentPanoramaPresenter(hud, repository, players, commands, bus, presence);
        await commands.ExecuteAsync("!anotournament", Viewer);
        StringAssert.Contains(hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_roster_0")!, "server-b");
        StringAssert.Contains(hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_team_status")!, "Fehlen 4");
        StringAssert.Contains(hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_roster_0")!, "Nicht bereit");
        presence.States = [presence.States[0] with { Players = [remotePlayer with { IsReady = true }] }];
        await presenter.RefreshOpenAsync();
        StringAssert.Contains(hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_roster_0")!, "Bereit für");
        presence.States = [];
        await presenter.RefreshOpenAsync();
        StringAssert.Contains(hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_roster_0")!, "Offline");
        Assert.AreEqual(1L, repository.Revision);
    }

    private sealed class Presence : ITournamentPresenceRepository
    {
        public IReadOnlyList<TournamentServerPresence> States { get; set; } = [];
        public ValueTask PublishAsync(string serverId, Guid instanceId, IReadOnlyCollection<TournamentPlayerPresence> players, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<TournamentServerPresence>> LoadOnlineAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(States);
    }

    [TestMethod]
    public async Task QualifiedFinalAppearsOnlyAfterEveryGroupFixtureIsConfirmed()
    {
        var bus = new AnoEventBus();
        var players = new PlayerRegistry(bus);
        await players.ConnectAsync(new(Viewer, "Viewer", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        var definition = Definition();
        definition.PlayoffQualifiersPerGroup = 2;
        var repository = new MemoryEvents(definition);
        var hud = new TestCustomHudService();
        var commands = new CommandRegistry(new Allow());
        using var presenter = new TournamentPanoramaPresenter(hud, repository, players, commands, bus);
        await commands.ExecuteAsync("!anotournament", Viewer);
        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_tab_bracket");
        StringAssert.Contains(hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_description")!, "warten");
        Assert.IsTrue(hud.Class(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_match_0", "hidden"));
        repository.GroupResults = TournamentRoundRobin.Generate(definition, "main").SelectMany(round => round.Fixtures)
            .Select(fixture => new TournamentGroupResult(fixture.Id, 1, 0, 13, 4)).ToArray();
        repository.Revision++;
        await presenter.RefreshOpenAsync();
        StringAssert.Contains(hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_match_0_text")!, "bereit");
        Assert.IsTrue(hud.Class(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_match_1", "hidden"));
    }

    [TestMethod]
    public async Task SelectedTeamSurvivesRankingChangesAndAllSubstitutesArePaged()
    {
        var bus = new AnoEventBus();
        var players = new PlayerRegistry(bus);
        await players.ConnectAsync(new(Viewer, "Viewer", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        var definition = Definition();
        definition.MaxSubstitutes = 5;
        definition.Teams[0].Substitutes = Enumerable.Range(0, 5).Select(i => 76561198001000000UL + (ulong)i).ToList();
        var repository = new MemoryEvents(definition);
        var hud = new TestCustomHudService();
        var commands = new CommandRegistry(new Allow());
        using var presenter = new TournamentPanoramaPresenter(hud, repository, players, commands, bus);
        await commands.ExecuteAsync("!anotournament", Viewer);
        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_tab_table");
        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_table_2");
        var selected = hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_team_name");
        repository.GroupResults = TournamentRoundRobin.Generate(definition, "main").SelectMany(round => round.Fixtures)
            .Select(fixture => new TournamentGroupResult(fixture.Id, fixture.HomeTeamId == "team-3" ? 1 : 0, fixture.HomeTeamId == "team-3" ? 0 : 1, 13, 4)).ToArray();
        repository.Revision++;
        await presenter.RefreshOpenAsync();
        Assert.AreEqual(selected, hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_team_name"));
        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_tab_teams");
        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_team_0");
        Assert.AreEqual("1 / 2", hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_page"));
        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_next");
        StringAssert.Contains(hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_roster_0")!, "Ersatz 3");
        StringAssert.Contains(hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_roster_2")!, definition.Teams[0].Substitutes[4].ToString());
    }

    [TestMethod]
    public async Task SwissStandingsAndMatchesUseOnlyThePublishedDynamicSchedule()
    {
        var bus = new AnoEventBus();
        var players = new PlayerRegistry(bus);
        await players.ConnectAsync(new(Viewer, "Viewer", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        var definition = Definition();
        definition.GroupFormat = TournamentGroupFormat.Swiss;
        definition.SwissRounds = 2;
        var repository = new MemoryEvents(definition);
        var hud = new TestCustomHudService();
        var commands = new CommandRegistry(new Allow());
        using var presenter = new TournamentPanoramaPresenter(hud, repository, players, commands, bus);
        await commands.ExecuteAsync("!anotournament", Viewer);

        Assert.AreEqual("1 Gruppenspiele", hud.Text(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_summary_2"));
        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_tab_matches");
        StringAssert.Contains(hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_fixture_0")!, "vs");
        Assert.AreEqual("", hud.Text(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_fixture_1"));

        var first = TournamentSchedules.Generate(definition, "main", []).SelectMany(round => round.Fixtures)
            .Select(fixture => new TournamentGroupResult(fixture.Id, 1, 0, 13, 4)).ToArray();
        repository.GroupResults = first;
        repository.Revision++;
        await presenter.RefreshOpenAsync();

        Assert.AreEqual("2 Gruppenspiele", hud.Text(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_summary_2"));
        StringAssert.Contains(hud.Text(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_fixture_1")!, "vs");
    }

    [TestMethod]
    public async Task DoubleEliminationShowsUpperLowerAndResetWithoutChangingWorkshopAssets()
    {
        var bus = new AnoEventBus();
        var players = new PlayerRegistry(bus);
        await players.ConnectAsync(new(Viewer, "Viewer", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        var definition = Definition();
        definition.GroupStageEnabled = false;
        definition.PlayoffFormat = TournamentPlayoffFormat.DoubleElimination;
        definition.GrandFinalReset = true;
        var repository = new MemoryEvents(definition);
        var hud = new TestCustomHudService();
        var commands = new CommandRegistry(new Allow());
        using var presenter = new TournamentPanoramaPresenter(hud, repository, players, commands, bus);

        Assert.IsTrue((await commands.ExecuteAsync("!anotournament", Viewer)).Success);
        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_tab_table");
        StringAssert.Contains(hud.Text(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_description")!, "Keine Gruppenphase");
        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId, "ano_tournament_tab_bracket");
        StringAssert.Contains(hud.Text(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_description")!, "Double Elimination");
        var contents = Enumerable.Range(0, 16).Select(i =>
            hud.Text(Viewer, TournamentPanoramaPresenter.HudId, $"ano_tournament_match_{i}_text") ?? "").ToArray();
        Assert.IsTrue(contents.Count(t => t.Contains(" vs ", StringComparison.Ordinal)) >= 4);
        Assert.IsTrue(contents.Any(t => t.Contains("Lower", StringComparison.Ordinal)));
        Assert.IsTrue(contents.Any(t => t.Contains("Reset", StringComparison.Ordinal)));
        Assert.IsTrue(contents.Any(t => t.Contains("Verlierer", StringComparison.Ordinal)));
    }

    private sealed class MemoryEvents(TournamentEventDefinition definition)
        : ITournamentEventRepository
    {
        public TournamentEventDefinition Definition { get; } = definition;
        public long Revision { get; set; } = 1;
        public IReadOnlyList<TournamentBracketResult> PlayoffResults { get; set; } = [];
        public IReadOnlyList<TournamentGroupResult> GroupResults { get; set; } = [];
        public ValueTask<TournamentStoredEvent?> LoadActiveAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<TournamentStoredEvent?>(new TournamentStoredEvent(Definition, GroupResults, Revision) { PlayoffResults = PlayoffResults });
        public ValueTask<TournamentStoredEvent?> LoadAsync(string eventId, CancellationToken cancellationToken = default)
            => LoadActiveAsync(cancellationToken);
        public ValueTask<TournamentStoredEvent> CreateAsync(TournamentEventDefinition definition,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public ValueTask<TournamentStoredEvent> SaveAsync(string eventId, long expectedRevision,
            TournamentEventDefinition definition, IReadOnlyCollection<TournamentGroupResult> confirmedResults,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public ValueTask ActivateAsync(string eventId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class Allow : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId,
            PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private static TournamentEventDefinition Definition()
    {
        var teams = Enumerable.Range(1, 3).Select(i =>
        {
            var starters = Enumerable.Range(0, 5)
                .Select(j => 76561198000910000UL + (ulong)(10 * i + j)).ToList();
            return new TournamentEventTeamDefinition
            {
                Id = $"team-{i}",
                Name = $"Squad {i}",
                Tag = $"S{i}",
                CaptainSteamId = starters[0],
                Starters = starters,
                InitialSeed = i,
            };
        }).ToList();
        return new TournamentEventDefinition
        {
            Id = "example",
            Name = "Example Cup",
            Teams = teams,
            Groups =
            [
                new TournamentGroupDefinition
                {
                    Id = "main",
                    Name = "Main group",
                    TeamIds = teams.Select(team => team.Id).ToList(),
                },
            ],
        };
    }
}
