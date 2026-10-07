using System.Text.Json;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Progression;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class GameplayXpModuleTests
{
    private static readonly PlayerId Player = new(76561198000262102);
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 17, 0, 0, TimeSpan.Zero);
    private static ProgressionDefinitionSnapshot Xp => ProgressionDefinitionSnapshot.Create([new(1, 0), new(2, 100)], []);

    [TestMethod]
    public void Policy_SnapshotsWeightsAndPersistsEligibilityStartThroughJson()
    {
        var configuration = new GameplayXpConfiguration { EarnFromUtc = Now };
        var policy = configuration.Snapshot();
        configuration.GameplayXp.Clear();
        configuration.KillXp = 999;
        Assert.AreEqual(10, policy.KillXp);
        Assert.AreEqual(20, policy.GameplayXp[GameplayStatKind.BombPlanted]);
        var restored = JsonSerializer.Deserialize<GameplayXpConfiguration>(JsonSerializer.Serialize(configuration))!;
        Assert.AreEqual(Now, restored.Snapshot().EarnFromUtc);
    }

    [TestMethod]
    public void Policy_RejectsUnknownNegativeUnboundedAndImpreciseInputs()
    {
        Assert.IsNotEmpty(GameplayXpConfiguration.Validate(new() { EarnFromUtc = Now.AddTicks(1) }));
        Assert.IsNotEmpty(GameplayXpConfiguration.Validate(new() { EarnFromUtc = Now.ToOffset(TimeSpan.FromHours(2)) }));
        Assert.IsNotEmpty(GameplayXpConfiguration.Validate(new() { BatchSize = 101 }));
        Assert.IsNotEmpty(GameplayXpConfiguration.Validate(new() { KillXp = -1 }));
        Assert.IsNotEmpty(GameplayXpConfiguration.Validate(new() { GameplayXp = new() { [(GameplayStatKind)255] = 1 } }));
        Assert.IsNotEmpty(GameplayXpConfiguration.Validate(null!));
    }

    [TestMethod]
    public async Task Checkpoints_IsolatePlayerFailureAndRetryOnNextTick()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        await players.ConnectAsync(new(new(Player.SteamId64 + 1), "Other", PlayerTeam.CounterTerrorist, true, Now));
        var repository = new Repository { FailPlayer = Player };
        using var module = new GameplayXpModule(new GameplayXpConfiguration().Snapshot(), Xp, players,
            repository, new Grants(), new CommandRegistry(new AllowAll()), _ => throw new InvalidOperationException("diagnostics"));
        await module.ReconcileOnlineAsync(Now);
        Assert.AreEqual(2, repository.Calls);
        repository.FailPlayer = null;
        await module.ReconcileOnlineAsync(Now);
        Assert.AreEqual(4, repository.Calls);
    }

    [TestMethod]
    public async Task Checkpoints_SkipOverlapAndStopWorkOnUnload()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        await players.ConnectAsync(new(new(Player.SteamId64 + 1), "Other", PlayerTeam.CounterTerrorist, true, Now));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Repository { BeforeRead = async () => { started.TrySetResult(); await release.Task; } };
        using var module = new GameplayXpModule(new GameplayXpConfiguration().Snapshot(), Xp, players,
            repository, new Grants(), new CommandRegistry(new AllowAll()));
        var first = module.ReconcileOnlineAsync(Now).AsTask();
        await started.Task;
        await module.ReconcileOnlineAsync(Now);
        module.Dispose();
        release.SetResult();
        await first;
        Assert.AreEqual(1, repository.Calls);
    }

    [TestMethod]
    public async Task Command_ShowsLifetimeXpAndCleansUpOnDispose()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var commands = new CommandRegistry(new AllowAll());
        using var module = new GameplayXpModule(new GameplayXpConfiguration().Snapshot(), Xp, players,
            new Repository(), new Grants(), commands);
        var result = await commands.ExecuteAsync("!anoxp", Player);
        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.Message!, "Level 2");
        StringAssert.Contains(result.Message!, "100");
        Assert.AreEqual(CommandFailureReason.Forbidden, (await commands.ExecuteAsync("!anoxp", null)).FailureReason);
        module.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound, (await commands.ExecuteAsync("!anoxp", Player)).FailureReason);
    }

    [TestMethod]
    public async Task Command_ReconnectDuringReadSuppressesStaleOutput()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var commands = new CommandRegistry(new AllowAll());
        var grants = new Grants { BeforeRead = async () => await Connect(players) };
        using var module = new GameplayXpModule(new GameplayXpConfiguration().Snapshot(), Xp, players,
            new Repository(), grants, commands);
        var result = await commands.ExecuteAsync("!anoxp", Player);
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Message);
    }

    [TestMethod]
    public async Task Checkpoint_PublishesOnlyNewCommittedGrantsInRevisionOrder()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await Connect(players);
        var seen = new List<long>();
        using var subscription = events.Subscribe<ProgressionXpGrantedEvent>((value, _) =>
        {
            seen.Add(value.Grant.AccountRevisionAfter);
            return ValueTask.CompletedTask;
        });
        using var module = new GameplayXpModule(new GameplayXpConfiguration().Snapshot(), Xp, players,
            new CommittedRepository(), new Grants(), new CommandRegistry(new AllowAll()), events: events);
        await module.ReconcileOnlineAsync(Now);
        await module.ReconcileOnlineAsync(Now);
        CollectionAssert.AreEqual(new long[] { 1, 2 }, seen);
    }

    private sealed class CommittedRepository : IGameplayXpRepository
    {
        private bool _committed;
        public ValueTask<IReadOnlyList<ProgressionGrantRecord>> ReconcileAsync(PlayerId playerId, GameplayXpPolicy policy,
            ProgressionDefinitionSnapshot definitions, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            if (_committed) return ValueTask.FromResult<IReadOnlyList<ProgressionGrantRecord>>([]);
            _committed = true;
            return ValueTask.FromResult<IReadOnlyList<ProgressionGrantRecord>>(
                [new(Player, "second", ProgressionXpSource.Gameplay, 100, 100, "test", Now, null, 1m, 200, 2),
                 new(Player, "first", ProgressionXpSource.Gameplay, 100, 100, "test", Now, null, 1m, 100, 1)]);
        }
    }

    private static async Task Connect(PlayerRegistry players)
        => _ = await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));

    private sealed class Repository : IGameplayXpRepository
    {
        public int Calls { get; private set; }
        public PlayerId? FailPlayer { get; set; }
        public Func<Task>? BeforeRead { get; init; }
        public async ValueTask<IReadOnlyList<ProgressionGrantRecord>> ReconcileAsync(PlayerId playerId, GameplayXpPolicy policy,
            ProgressionDefinitionSnapshot definitions, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (BeforeRead is not null) await BeforeRead();
            if (playerId == FailPlayer) throw new InvalidOperationException("test failure");
            return [];
        }
    }

    private sealed class Grants : IProgressionGrantRepository
    {
        public Func<Task>? BeforeRead { get; init; }
        public async ValueTask<ProgressionLifetimeState> ReadLifetimeAsync(PlayerId playerId, CancellationToken cancellationToken = default)
        {
            if (BeforeRead is not null) await BeforeRead();
            return new(playerId, 100, 1);
        }
        public ValueTask<ProgressionGrantRecord?> ReadGrantAsync(PlayerId playerId, string grantId, CancellationToken cancellationToken = default)
            => throw new AssertFailedException();
        public ValueTask<ProgressionGrantCommitResult> ApplyAsync(PlayerId playerId, ProgressionGrantCandidate candidate,
            CancellationToken cancellationToken = default) => throw new AssertFailedException();
    }

    private sealed class AllowAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }
}
