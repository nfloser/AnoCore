using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Menus;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class StatisticsMenuModuleTests
{
    private static readonly PlayerId Player = new(76561198000184501);
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 16, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Menus_SeparatePersonalSnapshotAndServerCategorySelector()
    {
        var setup = await CreateAsync();
        using var module = setup.Module;
        Assert.IsTrue((await setup.Commands.ExecuteAsync("anopersonalstatsmenu", Player)).Success);
        Assert.IsTrue(setup.Menus.TryGetOpenMenu(Player, out var personal));
        Assert.AreEqual("Personal Stats", personal!.Title);
        Assert.IsTrue(personal.Options.Any(option => option.Label.Contains("K/D/A: 100/40/20", StringComparison.Ordinal)));
        Assert.IsTrue(personal.Options.Any(option => option.Label.Contains("HS: 25.0%", StringComparison.Ordinal)));
        Assert.IsTrue(personal.Options.Any(option => option.Label.Contains("Win: 80.0%", StringComparison.Ordinal)));
        Assert.AreEqual(1, setup.Repository.PersonalReads);
        Assert.IsTrue((await setup.Commands.ExecuteAsync("anostatsmenu", Player)).Success);
        Assert.IsTrue(setup.Menus.TryGetOpenMenu(Player, out var stats));
        Assert.AreEqual("Stats", stats!.Title);
        Assert.IsTrue(stats.Options.Any(option => option.Label == "Kills"));
        Assert.AreEqual(0, setup.Repository.TopReads);
    }

    [TestMethod]
    public async Task Leaderboard_PaginatesOfflineEntriesHighlightsRequesterAndHasPlainTitle()
    {
        var setup = await CreateAsync();
        using var module = setup.Module;
        setup.Repository.Entries = Enumerable.Range(0, 7).Select(index =>
            new StatisticsRankEntry(new PlayerId(Player.SteamId64 + (ulong)index), 100 - index, index + 1,
                index == 0 ? "<Player>" : $"Offline {index}")).ToArray();
        await setup.Commands.ExecuteAsync("anostatsmenu", Player);
        setup.Menus.TryGetOpenMenu(Player, out var root);
        Assert.IsTrue((await setup.Menus.SelectAsync(Player, root!.Options.Single(option => option.Label == "Kills").Id)).Accepted);
        setup.Menus.TryGetOpenMenu(Player, out var first);
        Assert.AreEqual("Stats", first!.Title);
        Assert.IsTrue(first.Options.Any(option => option.Label.Contains("#1 &lt;Player&gt; (you)", StringComparison.Ordinal)));
        Assert.IsFalse(first.Options.Any(option => option.Label == "Previous page"));
        Assert.IsTrue((await setup.Menus.SelectAsync(Player, first.Options.Single(option => option.Label == "Next page").Id)).Accepted);
        setup.Menus.TryGetOpenMenu(Player, out var second);
        Assert.AreEqual("Stats", second!.Title);
        Assert.IsTrue(second.Options.Any(option => option.Label.Contains("#6 Offline 5", StringComparison.Ordinal)));
        Assert.IsTrue(second.Options.Any(option => option.Label == "Previous page"));
        Assert.IsFalse(second.Options.Any(option => option.Label == "Next page"));
        Assert.AreEqual((6, 5), setup.Repository.LastPagination);
        Assert.IsFalse((await setup.Menus.SelectAsync(Player, first.Options[0].Id)).Accepted);
    }

    [TestMethod]
    public async Task EmptyAndSinglePage_ShowNoFalseNavigation()
    {
        var setup = await CreateAsync();
        using var module = setup.Module;
        foreach (var count in new[] { 0, 1, 5 })
        {
            setup.Repository.Entries = Enumerable.Range(0, count).Select(index =>
                new StatisticsRankEntry(Player, index + 1, index + 1, "Name")).ToArray();
            await setup.Commands.ExecuteAsync("anostatsmenu", Player);
            setup.Menus.TryGetOpenMenu(Player, out var root);
            await setup.Menus.SelectAsync(Player, root!.Options.Single(option => option.Label == "Kills").Id);
            setup.Menus.TryGetOpenMenu(Player, out var page);
            Assert.IsFalse(page!.Options.Any(option => option.Label is "Next page" or "Previous page"));
            if (count == 0) Assert.IsTrue(page.Options.Any(option => option.Label.Contains("No recorded players", StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    public async Task InFlightRead_CannotReplaceAnotherMenuOrAReconnectedSession()
    {
        var setup = await CreateAsync();
        using var module = setup.Module;
        setup.Repository.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Repository.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = setup.Commands.ExecuteAsync("anopersonalstatsmenu", Player).AsTask();
        await setup.Repository.Started.Task;
        var other = new MenuDefinition(new("ano.other"), "Other", []);
        using var registered = setup.Menus.Register(new("other"), other);
        setup.Menus.Open(Player, other.Id);
        setup.Repository.Release.SetResult(true);
        Assert.IsFalse((await pending).Success);
        setup.Menus.TryGetOpenMenu(Player, out var stillOther);
        Assert.AreSame(other, stillOther);
        setup.Repository.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Repository.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = setup.Commands.ExecuteAsync("anopersonalstatsmenu", Player).AsTask();
        await setup.Repository.Started.Task;
        await setup.Players.ConnectAsync(new(Player, "Reconnect", PlayerTeam.Terrorist, true, Now.AddSeconds(1)));
        setup.Repository.Release.SetResult(true);
        Assert.IsFalse((await pending).Success);
    }

    [TestMethod]
    public async Task RankedMenus_UseTheExistingLedgerAndBackupWeights()
    {
        var setup = await CreateAsync();
        using var module = setup.Module;
        var ranks = new Ranks();
        var configuration = new RankConfiguration
        {
            Source = RankScoreSource.EventLedger,
            StartingPoints = 1000,
            KillPoints = 2,
            AssistPoints = 1,
            DeathPenalty = 2,
            GameplayPoints = new() { [GameplayStatKind.Mvp] = 1 },
        };
        module.EnableRanks(configuration, ranks);
        await setup.Commands.ExecuteAsync("anostatsmenu", Player);
        setup.Menus.TryGetOpenMenu(Player, out var root);
        await setup.Menus.SelectAsync(Player, root!.Options.Single(option => option.Label == "Rank points").Id);
        setup.Menus.TryGetOpenMenu(Player, out var top);
        Assert.IsTrue(top!.Options.Any(option => option.Label.Contains("1050", StringComparison.Ordinal)));
        Assert.AreEqual(RankScoreSource.EventLedger, ranks.Weights!.Source);
        Assert.AreEqual(1000L, ranks.Weights.StartingPoints);
        Assert.AreEqual(2, ranks.Weights.KillPoints);
        Assert.AreEqual(2, ranks.Weights.DeathPenalty);
        await setup.Commands.ExecuteAsync("anopersonalstatsmenu", Player);
        setup.Menus.TryGetOpenMenu(Player, out var personal);
        Assert.IsTrue(personal!.Options.Any(option => option.Label == "Rank points: 1050 · #1"));
    }

    [TestMethod]
    public async Task UnloadAndClosedMenuDiscardPendingReadAndReleaseCommands()
    {
        var setup = await CreateAsync();
        using var module = setup.Module;
        await setup.Commands.ExecuteAsync("anostatsmenu", Player);
        setup.Repository.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Repository.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = setup.Commands.ExecuteAsync("anopersonalstatsmenu", Player).AsTask();
        await setup.Repository.Started.Task;
        setup.Menus.Close(Player);
        setup.Repository.Release.SetResult(true);
        Assert.IsFalse((await pending).Success);
        setup.Repository.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Repository.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = setup.Commands.ExecuteAsync("anopersonalstatsmenu", Player).AsTask();
        await setup.Repository.Started.Task;
        module.Dispose();
        setup.Repository.Release.SetResult(true);
        Assert.IsFalse((await pending).Success);
        Assert.AreEqual(CommandFailureReason.NotFound, (await setup.Commands.ExecuteAsync("anostatsmenu", Player)).FailureReason);
    }

    [TestMethod]
    public async Task GameplayCompositionOwnsAllStatisticsViewsAndRollsBackACollision()
    {
        var setup = await CreateAsync();
        setup.Module.Dispose();
        using (var gameplay = new GameplayStatsModule(setup.Commands, setup.Players, setup.Repository,
            combat: new Ranks(), menus: setup.Menus))
        {
            Assert.IsTrue(setup.Commands.GetCommands().Any(command => command.Name == "anostatdetails"));
            Assert.IsTrue((await setup.Commands.ExecuteAsync("anostatsmenu", Player)).Success);
            Assert.IsTrue((await setup.Commands.ExecuteAsync("anopersonalstatsmenu", Player)).Success);
        }
        Assert.AreEqual(CommandFailureReason.NotFound, (await setup.Commands.ExecuteAsync("anopersonalstatsmenu", Player)).FailureReason);
        using var reserved = setup.Commands.Register(new("reserved"), new("anopersonalstatsmenu", "Reserved"), _ => ValueTask.FromResult(CommandResult.Ok()));
        Assert.ThrowsExactly<InvalidOperationException>(() => new GameplayStatsModule(setup.Commands, setup.Players, setup.Repository,
            combat: new Ranks(), menus: setup.Menus));
        Assert.AreEqual(CommandFailureReason.NotFound, (await setup.Commands.ExecuteAsync("anostatsmenu", Player)).FailureReason);
        Assert.AreEqual(CommandFailureReason.NotFound, (await setup.Commands.ExecuteAsync("anostatdetails", Player)).FailureReason);
    }

    private static async Task<(StatisticsMenuModule Module, CommandRegistry Commands, MenuService Menus, PlayerRegistry Players, Repository Repository)> CreateAsync()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new Allow());
        var menus = new MenuService();
        var repository = new Repository();
        return (new StatisticsMenuModule(commands, players, menus, repository, events), commands, menus, players, repository);
    }

    private sealed class Allow : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class Repository : IStatisticsMenuRepository, IGameplayStatRepository
    {
        public IReadOnlyList<StatisticsRankEntry> Entries { get; set; } = [];
        public int PersonalReads { get; private set; }
        public int TopReads { get; private set; }
        public (int Limit, int Offset) LastPagination { get; private set; }
        public TaskCompletionSource<bool>? Started { get; set; }
        public TaskCompletionSource<bool>? Release { get; set; }
        public ValueTask RecordAsync(GameplayStatEvent statistic, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<GameplayStatTotal>> ReadAsync(PlayerId playerId, GameplayStatFilter? filter = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<GameplayStatTotal>>([]);
        public async ValueTask<PersonalStatistics> ReadPersonalAsync(PlayerId player, CancellationToken cancellationToken = default)
        {
            PersonalReads++;
            Started?.TrySetResult(true);
            if (Release is not null) await Release.Task.WaitAsync(cancellationToken);
            return new(new(100, 40, 20), TimeSpan.FromHours(10), 25, 100, 8, 2, 30, 4);
        }
        public ValueTask<IReadOnlyList<StatisticsRankEntry>> GetTopAsync(StatisticsCategory category, int limit, int offset, CancellationToken cancellationToken = default)
        {
            TopReads++;
            LastPagination = (limit, offset);
            return ValueTask.FromResult<IReadOnlyList<StatisticsRankEntry>>(Entries.Skip(offset).Take(limit).ToArray());
        }
    }

    private sealed class Ranks : IGameplayRankScoreRepository
    {
        public RankScoreWeights? Weights { get; private set; }
        public ValueTask<CombatScoreRankEntry?> GetScorePlacementAsync(PlayerId playerId, RankScoreWeights weights, CancellationToken cancellationToken = default)
        {
            Weights = weights;
            return ValueTask.FromResult<CombatScoreRankEntry?>(new(playerId, 1050, 1, "Player"));
        }
        public ValueTask<IReadOnlyList<CombatScoreRankEntry>> GetTopScoresAsync(RankScoreWeights weights, int limit, int offset, CancellationToken cancellationToken = default)
        {
            Weights = weights;
            return ValueTask.FromResult<IReadOnlyList<CombatScoreRankEntry>>([new(Player, 1050, 1, "Player")]);
        }
        public ValueTask<long> ReadRawScoreAsync(PlayerId playerId, RankScoreWeights weights, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask RecordAsync(CombatDeath death, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CombatTotals> ReadAsync(PlayerId playerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<CombatRankEntry>> GetTopKillsAsync(int limit, int offset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopDeathsAsync(int limit, int offset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopAssistsAsync(int limit, int offset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
