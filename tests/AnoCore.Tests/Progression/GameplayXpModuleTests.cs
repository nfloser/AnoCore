using System.Text.Json;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Progression;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Configuration;
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

    [TestMethod]
    public async Task StatusShowsStrongestCurrentBoostAndRemainingLevelXp()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var commands = new CommandRegistry(new AllowAll());
        var at = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var definitions = ProgressionDefinitionSnapshot.Create([new(1, 0), new(2, 100), new(3, 300)],
            [new("scheduled", at.AddHours(-1), at.AddHours(1), 3m)]);
        using var module = new GameplayXpModule(new GameplayXpConfiguration { WeekendMultiplier = 2m }.Snapshot(),
            definitions, players, new Repository(), new Grants(), commands, clock: () => at);
        var status = await commands.ExecuteAsync("!anoxp", Player);
        StringAssert.Contains(status.Message!, "200 XP to level 3");
        StringAssert.Contains(status.Message!, "3x (scheduled)");
        at = at.AddHours(1);
        status = await commands.ExecuteAsync("!anoxp", Player);
        StringAssert.Contains(status.Message!, "2x (gameplay.weekend.20261010)");
        at = at.AddDays(2);
        status = await commands.ExecuteAsync("!anoxp", Player);
        StringAssert.Contains(status.Message!, "1x (none)");
    }

    [TestMethod]
    public async Task ReloadChangesCheckpointWeightsAndCurrentBoostWithoutReplacingOwnedCommands()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var commands = new CommandRegistry(new AllowAll());
        var reloads = new ConfigReloadRegistry();
        var store = new Configuration();
        var repository = new Repository();
        using var module = new GameplayXpModule(store.Value.Snapshot(), Xp, players, repository, new Grants(), commands,
            clock: () => new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero), reloads: reloads, configuration: store);
        store.Value.KillXp = 42;
        store.Value.WeekendMultiplier = 3m;
        await reloads.ReloadAsync("gameplay-xp");
        await module.ReconcileOnlineAsync(Now);
        Assert.AreEqual(42, repository.Policies.Single().KillXp);
        StringAssert.Contains((await commands.ExecuteAsync("!anoxp", Player)).Message!, "3x (gameplay.weekend.");
        Assert.HasCount(1, reloads.Configurations);
        module.Dispose();
        Assert.IsEmpty(reloads.Configurations);
        Assert.AreEqual(CommandFailureReason.NotFound, (await commands.ExecuteAsync("!anoxp", Player)).FailureReason);
    }

    [TestMethod]
    public async Task ReloadDuringCheckpointPinsOneSnapshotAcrossAllPlayers()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        await players.ConnectAsync(new(new(Player.SteamId64 + 1), "Other", PlayerTeam.CounterTerrorist, true, Now));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Repository { BeforeRead = async () => { started.TrySetResult(); await release.Task; } };
        var reloads = new ConfigReloadRegistry();
        var store = new Configuration();
        using var module = new GameplayXpModule(store.Value.Snapshot(), Xp, players, repository, new Grants(),
            new CommandRegistry(new AllowAll()), reloads: reloads, configuration: store);
        var checkpoint = module.ReconcileOnlineAsync(Now).AsTask();
        await started.Task;
        store.Value.KillXp = 42;
        await reloads.ReloadAsync("gameplay-xp");
        release.SetResult();
        await checkpoint;
        CollectionAssert.AreEqual(new[] { 10, 10 }, repository.Policies.Select(item => item.KillXp).ToArray());
        await module.ReconcileOnlineAsync(Now);
        CollectionAssert.AreEqual(new[] { 10, 10, 42, 42 }, repository.Policies.Select(item => item.KillXp).ToArray());
    }

    [TestMethod]
    [DataRow("disabled")]
    [DataRow("interval")]
    [DataRow("start")]
    [DataRow("invalid")]
    public async Task InvalidOrRestartOnlyChangesPreserveActivePolicy(string change)
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var reloads = new ConfigReloadRegistry();
        var store = new Configuration();
        var repository = new Repository();
        using var module = new GameplayXpModule(store.Value.Snapshot(), Xp, players, repository, new Grants(),
            new CommandRegistry(new AllowAll()), reloads: reloads, configuration: store);
        store.Value.KillXp = 42;
        switch (change)
        {
            case "disabled": store.Value.Enabled = false; break;
            case "interval": store.Value.CheckpointSeconds = 60; break;
            case "start": store.Value.EarnFromUtc = Now.AddDays(-1); break;
            case "invalid": store.Value.KillXp = -1; break;
        }
        await Assert.ThrowsExactlyAsync<ConfigValidationException>(async () => await reloads.ReloadAsync("gameplay-xp"));
        await module.ReconcileOnlineAsync(Now);
        Assert.AreEqual(10, repository.Policies.Single().KillXp);
        Assert.AreEqual(30, module.CheckpointSeconds);
    }

    [TestMethod]
    public async Task CommandCollisionRollsBackReloadRegistrationAndPartialServicesAreRejected()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var commands = new CommandRegistry(new AllowAll());
        var reloads = new ConfigReloadRegistry();
        var store = new Configuration();
        using var first = new GameplayXpModule(store.Value.Snapshot(), Xp, players, new Repository(), new Grants(), commands);
        Assert.ThrowsExactly<InvalidOperationException>(() => new GameplayXpModule(store.Value.Snapshot(), Xp, players,
            new Repository(), new Grants(), commands, reloads: reloads, configuration: store));
        Assert.IsEmpty(reloads.Configurations);
        Assert.ThrowsExactly<ArgumentException>(() => new GameplayXpModule(store.Value.Snapshot(), Xp, players,
            new Repository(), new Grants(), commands, reloads: reloads));
        Assert.IsTrue((await commands.ExecuteAsync("!anoxp", Player)).Success);
    }

    private sealed class Configuration : IConfigStore
    {
        public GameplayXpConfiguration Value { get; } = new() { Enabled = true, EarnFromUtc = Now };
        public ValueTask<T> LoadAsync<T>(string name, Func<T> createDefault,
            Func<T, IReadOnlyCollection<string>>? validate = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = (T)(object)Value;
            var errors = validate?.Invoke(value) ?? [];
            if (errors.Count > 0) throw new ConfigValidationException(name, errors);
            return ValueTask.FromResult(value);
        }
        public ValueTask SaveAsync<T>(string name, T value, Func<T, IReadOnlyCollection<string>>? validate = null,
            CancellationToken cancellationToken = default) => throw new AssertFailedException();
    }

    private static async Task Connect(PlayerRegistry players)
        => _ = await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));

    private sealed class Repository : IGameplayXpRepository
    {
        public int Calls { get; private set; }
        public List<GameplayXpPolicy> Policies { get; } = [];
        public PlayerId? FailPlayer { get; set; }
        public Func<Task>? BeforeRead { get; init; }
        public async ValueTask<IReadOnlyList<ProgressionGrantRecord>> ReconcileAsync(PlayerId playerId, GameplayXpPolicy policy,
            ProgressionDefinitionSnapshot definitions, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            Calls++;
            Policies.Add(policy);
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
