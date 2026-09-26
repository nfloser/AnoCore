using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class RankModuleTests
{
    private static readonly PlayerId Player = new(76561198000012601);
    private string _root = null!;

    [TestInitialize]
    public void Initialize() => _root = Path.Combine(Path.GetTempPath(),
        "anocore-rank-tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task RankCommand_UsesPersistedTotalsAndConfiguredExactThreshold()
    {
        var store = new JsonConfigStore(_root);
        await store.SaveAsync("ranks", new RankConfiguration
        {
            KillPoints = 3,
            AssistPoints = 1,
            DeathPenalty = 2,
            Thresholds = [new RankThreshold("Recruit", 0), new RankThreshold("Veteran", 10),
                new RankThreshold("Elite", 20)],
        });
        var players = new PlayerRegistry(new AnoEventBus());
        var commands = new CommandRegistry(new AllowAll());
        var repository = new FakeRepository(new CombatTotals(4, 2, 2));
        using var module = await RankModule.CreateAsync(store, commands, players, repository);
        Assert.AreEqual(CommandFailureReason.InvalidInput,
            (await commands.ExecuteAsync("!anorank", null)).FailureReason);
        await players.ConnectAsync(new PlayerConnection(Player, "Player", PlayerTeam.Terrorist,
            true, DateTimeOffset.UtcNow));
        var result = await commands.ExecuteAsync("!anorank", Player);
        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.Message!, "Veteran");
        StringAssert.Contains(result.Message!, "10 point(s) to Elite");
        Assert.AreEqual(Player, repository.LastRead);
        module.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anorank", Player)).FailureReason);
    }

    [TestMethod]
    public async Task RankCommand_ReportsHighestConfiguredRank()
    {
        var store = new JsonConfigStore(_root);
        await store.SaveAsync("ranks", new RankConfiguration
        {
            Thresholds = [new RankThreshold("Only", 0)],
        });
        var players = new PlayerRegistry(new AnoEventBus());
        await players.ConnectAsync(new PlayerConnection(Player, "Player", PlayerTeam.Terrorist,
            true, DateTimeOffset.UtcNow));
        var commands = new CommandRegistry(new AllowAll());
        using var module = await RankModule.CreateAsync(store, commands, players,
            new FakeRepository(new CombatTotals(0, 0, 0)));

        var result = await commands.ExecuteAsync("!anorank", Player);

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.Message!, "Highest configured rank reached");
    }

    [TestMethod]
    public async Task InvalidThresholds_RejectCompositionWithoutCommandLeak()
    {
        var store = new JsonConfigStore(_root);
        await store.SaveAsync("ranks", new RankConfiguration
        {
            Thresholds = [new RankThreshold("Recruit", 0), new RankThreshold("Broken", 0)],
        });
        var commands = new CommandRegistry(new AllowAll());
        await Assert.ThrowsExactlyAsync<ConfigValidationException>(async () =>
            await RankModule.CreateAsync(store, commands, new PlayerRegistry(new AnoEventBus()),
                new FakeRepository(new CombatTotals(0, 0, 0))));
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anorank", Player)).FailureReason);
    }

    [TestMethod]
    public async Task RankLeaderboard_UsesConfiguredWeightsPagesAndSanitizesNames()
    {
        var store = new JsonConfigStore(_root);
        await store.SaveAsync("ranks", new RankConfiguration
        {
            KillPoints = 3,
            AssistPoints = 1,
            DeathPenalty = 2,
            Thresholds = [new RankThreshold("Recruit", 0), new RankThreshold("Veteran", 10)],
        });
        var commands = new CommandRegistry(new AllowAll());
        var entries = new[]
        {
            new CombatScoreRankEntry(Player, 10, 6, "Name|With\nControl"),
        };
        var repository = new FakeRepository(new CombatTotals(0, 0, 0), entries);
        using var module = await RankModule.CreateAsync(store, commands,
            new PlayerRegistry(new AnoEventBus()), repository);

        var result = await commands.ExecuteAsync("!anotopranks 2", null);

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.Message!, "6. Name/With Control");
        StringAssert.Contains(result.Message!, "Veteran, 10 point");
        Assert.AreEqual((3, 1, 2, 5, 5), repository.LastScoreQuery);
        Assert.AreEqual(CommandFailureReason.InvalidInput,
            (await commands.ExecuteAsync("!anotopranks 0", null)).FailureReason);
    }

    [TestMethod]
    public async Task TopRankCommandCollision_RollsBackOwnRankCommand()
    {
        var commands = new CommandRegistry(new AllowAll());
        using var collision = commands.Register(new ModuleId("test.collision"),
            new CommandDescriptor("anotopranks", "Reserved for collision test."),
            _ => ValueTask.FromResult(CommandResult.Ok()));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await RankModule.CreateAsync(new JsonConfigStore(_root), commands,
                new PlayerRegistry(new AnoEventBus()),
                new FakeRepository(new CombatTotals(0, 0, 0))));

        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anorank", Player)).FailureReason);
    }

    [TestMethod]
    public void Score_FloorsAtZeroAndRejectsOverflow()
    {
        var policy = RankConfiguration.Default;
        Assert.AreEqual(0, policy.Score(new CombatTotals(0, 100, 0)));
        Assert.ThrowsExactly<OverflowException>(() =>
            policy.Score(new CombatTotals(long.MaxValue, 0, 0)));
        Assert.AreEqual("Veteran", policy.NextAfter(0)!.Name);
        Assert.AreEqual("Elite", policy.NextAfter(10)!.Name);
        Assert.IsNull(policy.NextAfter(100));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => policy.NextAfter(-1));
    }

    private sealed class AllowAll : AnoCore.Abstractions.Permissions.IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id,
            AnoCore.Abstractions.Permissions.PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class FakeRepository(CombatTotals totals,
        IReadOnlyList<CombatScoreRankEntry>? scores = null) : ICombatRepository
    {
        public PlayerId? LastRead { get; private set; }
        public (int Kill, int Assist, int Death, int Limit, int Offset)? LastScoreQuery { get; private set; }
        public ValueTask RecordAsync(CombatDeath death, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public ValueTask<CombatTotals> ReadAsync(PlayerId id, CancellationToken cancellationToken = default)
        {
            LastRead = id;
            return ValueTask.FromResult(totals);
        }
        public ValueTask<IReadOnlyList<CombatRankEntry>> GetTopKillsAsync(int limit, int offset,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatRankEntry>>([]);
        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopDeathsAsync(int limit, int offset,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatCountRankEntry>>([]);
        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopAssistsAsync(int limit, int offset,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatCountRankEntry>>([]);
        public ValueTask<IReadOnlyList<CombatScoreRankEntry>> GetTopScoresAsync(
            int killPoints, int assistPoints, int deathPenalty, int limit, int offset,
            CancellationToken cancellationToken = default)
        {
            LastScoreQuery = (killPoints, assistPoints, deathPenalty, limit, offset);
            return ValueTask.FromResult(scores ?? (IReadOnlyList<CombatScoreRankEntry>)[]);
        }
    }
}
