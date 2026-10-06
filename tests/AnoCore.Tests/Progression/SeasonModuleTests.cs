using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Progression;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class SeasonModuleTests
{
    private static readonly PlayerId Player = new(76561198000270101);
    private static readonly DateTimeOffset Start = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly SeasonDefinition Definition = new("s1", 1, "Season One", Start, Start.AddDays(31));
    private static ProgressionDefinitionSnapshot Xp => ProgressionDefinitionSnapshot.Create([new(1, 0), new(2, 100)], []);

    [TestMethod]
    public void Configuration_ValidatesWindowsBoundsAndSnapshotsLists()
    {
        var config = new SeasonConfiguration { Seasons = [Definition] };
        var snapshot = config.Snapshot();
        config.Seasons.Clear();
        Assert.HasCount(1, snapshot.Catalog.Seasons);
        Assert.IsFalse(config.Enabled);
        Assert.IsNotEmpty(SeasonConfiguration.Validate(new() { CheckpointSeconds = 1 }));
        Assert.IsNotEmpty(SeasonConfiguration.Validate(new() { BatchSize = 101 }));
        Assert.IsNotEmpty(SeasonConfiguration.Validate(new() { Seasons = [Definition, Definition with { Id = "overlap" }] }));
        Assert.IsNotEmpty(SeasonConfiguration.Validate(null!));
    }

    [TestMethod]
    public async Task Startup_AcceptsDefinitionsBeforeRegisteringCommands()
    {
        var repository = new Seasons();
        var commands = new CommandRegistry(new Permissions());
        using var module = await Create(new PlayerRegistry(new AnoEventBus()), repository, new Rewards(), commands);
        Assert.HasCount(1, repository.Accepted);
        Assert.HasCount(4, commands.GetCommands());
        module.Dispose();
        Assert.IsEmpty(commands.GetCommands());
    }

    [TestMethod]
    public async Task Commands_ReadOwnCurrentHistoricalCatalogAndGlobalRanking()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var repository = new Seasons();
        var commands = new CommandRegistry(new Permissions());
        using var module = await Create(players, repository, new Rewards(), commands);
        var own = await commands.ExecuteAsync("!anoseason", Player);
        StringAssert.Contains(own.Message!, "Level 2");
        StringAssert.Contains(own.Message!, "150 XP");
        Assert.IsTrue((await commands.ExecuteAsync("!anoseason s1", Player)).Success);
        StringAssert.Contains((await commands.ExecuteAsync("!anoseasons", Player)).Message!, "s1");
        StringAssert.Contains((await commands.ExecuteAsync("!anoseasontop s1 2", Player)).Message!, "#6");
        Assert.AreEqual(CommandFailureReason.Forbidden, (await commands.ExecuteAsync("!anoseason", null)).FailureReason);
        Assert.AreEqual(CommandFailureReason.InvalidInput, (await commands.ExecuteAsync("!anoseason missing", Player)).FailureReason);
        Assert.AreEqual(CommandFailureReason.InvalidInput, (await commands.ExecuteAsync("!anoseasons 0", Player)).FailureReason);
        Assert.AreEqual(CommandFailureReason.InvalidInput, (await commands.ExecuteAsync("!anoseasontop s1 20002", Player)).FailureReason);
    }

    [TestMethod]
    public async Task NoActiveSeason_ReportsWithoutCreatingXp()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var commands = new CommandRegistry(new Permissions());
        using var module = await Create(players, new Seasons(), new Rewards(), commands, () => Start.AddDays(40));
        StringAssert.Contains((await commands.ExecuteAsync("!anoseason", Player)).Message!, "No active season");
        Assert.IsTrue((await commands.ExecuteAsync("!anoseason s1", Player)).Success);
    }

    [TestMethod]
    public async Task ReconnectDuringRead_SuppressesOwnOutput()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var commands = new CommandRegistry(new Permissions());
        using var module = await Create(players, new Seasons { BeforeRead = async () => await Connect(players) }, new Rewards(), commands);
        Assert.IsFalse((await commands.ExecuteAsync("!anoseason", Player)).Success);
    }

    [TestMethod]
    public async Task Close_RequiresPermissionAndEndedWindow()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var permissions = new Permissions { Allow = false };
        var commands = new CommandRegistry(permissions);
        var repository = new Seasons();
        var clock = Start;
        using var module = await Create(players, repository, new Rewards(), commands, () => clock);
        Assert.IsFalse((await commands.ExecuteAsync("!anocloseseason s1", Player)).Success);
        Assert.AreEqual(0, repository.Closed);
        permissions.Allow = true;
        Assert.IsFalse((await commands.ExecuteAsync("!anocloseseason s1", Player)).Success);
        clock = Definition.EndsAtUtc;
        Assert.IsTrue((await commands.ExecuteAsync("!anocloseseason s1", Player)).Success);
        Assert.AreEqual(1, repository.Closed);
    }

    [TestMethod]
    public async Task GlobalCheckpoint_IsolatesSeasonFailuresWithoutOnlinePlayers()
    {
        var repository = new Seasons();
        var rewards = new Rewards { FailId = "s1" };
        using var module = await Create(new PlayerRegistry(new AnoEventBus()), repository, rewards,
            new CommandRegistry(new Permissions()));
        repository.Accepted.Add(new(Definition with { Id = "s2", StartsAtUtc = Definition.EndsAtUtc,
            EndsAtUtc = Definition.EndsAtUtc.AddDays(31) }, Start.AddDays(-7), null));
        await module.ReconcileAsync(Start.AddDays(40));
        CollectionAssert.AreEqual(new[] { "s1", "s2" }, rewards.Reconciled);
    }

    [TestMethod]
    public async Task Checkpoint_SkipsOverlapAndCancelsAfterUnload()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rewards = new Rewards { Before = async () => { started.TrySetResult(); await release.Task; } };
        using var module = await Create(new PlayerRegistry(new AnoEventBus()), new Seasons(), rewards,
            new CommandRegistry(new Permissions()));
        var first = module.ReconcileAsync(Start).AsTask();
        await started.Task;
        await module.ReconcileAsync(Start);
        module.Dispose();
        release.SetResult();
        await first;
        Assert.HasCount(1, rewards.Reconciled);
    }

    [TestMethod]
    public async Task RegistrationCollision_RollsBackEarlierCommands()
    {
        var commands = new CommandRegistry(new Permissions());
        using var reserved = commands.Register(new AnoCore.Abstractions.Modules.ModuleId("reserved"),
            new("anoseasons", "Reserved"), _ => ValueTask.FromResult(CommandResult.Ok()));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Create(new PlayerRegistry(new AnoEventBus()), new Seasons(), new Rewards(), commands));
        Assert.HasCount(1, commands.GetCommands());
    }

    private static ValueTask<SeasonModule> Create(PlayerRegistry players, Seasons seasons, Rewards rewards,
        CommandRegistry commands, Func<DateTimeOffset>? clock = null)
        => SeasonModule.CreateAsync(new SeasonConfiguration { Enabled = true, Seasons = [Definition] }.Snapshot(),
            Xp, players, seasons, new Progression(), rewards, commands, Start.AddDays(-7),
            clock: clock ?? (() => Start));

    private static async Task Connect(PlayerRegistry players)
        => _ = await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Start));

    private sealed class Permissions : IPermissionEvaluator
    {
        public bool Allow { get; set; } = true;
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Allow);
    }

    private sealed class Seasons : ISeasonRepository
    {
        public List<PersistedSeason> Accepted { get; } = [];
        public Func<Task>? BeforeRead { get; init; }
        public int Closed { get; private set; }
        public async ValueTask<IReadOnlyList<PersistedSeason>> ListEffectiveAsync(CancellationToken cancellationToken = default)
        {
            if (BeforeRead is not null) await BeforeRead();
            return Accepted.ToArray();
        }
        public ValueTask<PersistedSeason?> ReadAsync(string seasonId, int version, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Accepted.FirstOrDefault(item => item.Definition.Id == seasonId && item.Definition.Version == version));
        public ValueTask<SeasonAcceptResult> AcceptAsync(SeasonDefinition definition, DateTimeOffset acceptedAtUtc,
            CancellationToken cancellationToken = default)
        {
            var season = new PersistedSeason(definition, acceptedAtUtc, null);
            Accepted.Add(season);
            return ValueTask.FromResult(new SeasonAcceptResult(true, season));
        }
        public ValueTask<PersistedSeason> CloseAsync(string seasonId, int version, DateTimeOffset closedAtUtc,
            CancellationToken cancellationToken = default)
        {
            Closed++;
            var season = Accepted.Single(item => item.Definition.Id == seasonId);
            return ValueTask.FromResult(season with { ClosedAtUtc = closedAtUtc });
        }
    }

    private sealed class Progression : ISeasonProgressionRepository
    {
        public ValueTask<SeasonProgressionState> ReadAsync(PlayerId playerId, string seasonId, int seasonVersion,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new SeasonProgressionState(playerId, seasonId, seasonVersion, 150, 1));
        public ValueTask<SeasonXpGrantRecord?> ReadGrantAsync(PlayerId playerId, string seasonId, string grantId,
            CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public ValueTask<SeasonXpGrantCommitResult> ApplyAsync(PlayerId playerId, SeasonXpGrantCandidate candidate,
            CancellationToken cancellationToken = default) => throw new AssertFailedException();
    }

    private sealed class Rewards : ISeasonRewardRepository
    {
        public string? FailId { get; init; }
        public Func<Task>? Before { get; init; }
        public List<string> Reconciled { get; } = [];
        public async ValueTask<int> ReconcileAsync(PersistedSeason season, int batchSize, DateTimeOffset at,
            CancellationToken cancellationToken = default)
        {
            Reconciled.Add(season.Definition.Id);
            if (Before is not null) await Before();
            if (season.Definition.Id == FailId) throw new InvalidOperationException("test failure");
            return 0;
        }
        public ValueTask<IReadOnlyList<SeasonLeaderboardEntry>> ReadTopAsync(string seasonId, int seasonVersion,
            int offset, int limit, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<SeasonLeaderboardEntry>>([new(offset + 1L, Player, 150)]);
    }
}
