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
        await hud.ClickAsync(Viewer, TournamentPanoramaPresenter.HudId,
            "ano_tournament_close");
        Assert.AreEqual(0, hud.VisiblePlayers(TournamentPanoramaPresenter.HudId).Count);
    }

    private sealed class MemoryEvents(TournamentEventDefinition definition)
        : ITournamentEventRepository
    {
        public TournamentEventDefinition Definition { get; } = definition;
        public long Revision { get; set; } = 1;
        public ValueTask<TournamentStoredEvent?> LoadActiveAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<TournamentStoredEvent?>(new(Definition, [], Revision));
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
