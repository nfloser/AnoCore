using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Progression;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Runtime.Settings;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class AchievementModuleTests
{
    private static readonly PlayerId Player = new(76561198000254101);
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 16, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Checkpoint_NotifiesOnlyCommittedUnlocksAndCleansUpToggle()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var toggles = new PlayerToggleCatalog();
        var messages = new Messages();
        var unlocks = new Unlocks { AwardFirst = true };
        using var module = new AchievementModule(new AchievementConfiguration().Snapshot(), players,
            new Stats(), unlocks, new Grants(), new CommandRegistry(new AllowAll()),
            settings: new Settings(), toggles: toggles, messages: messages);
        await module.ReconcileOnlineAsync(Now);
        await module.ReconcileOnlineAsync(Now);
        Assert.HasCount(1, messages.Requests);
        Assert.AreEqual(3, unlocks.Calls);
        module.Dispose();
        Assert.IsEmpty(toggles.GetAll());
    }

    [TestMethod]
    public void Configuration_ValidatesCatalogAndSnapshotsMutableLists()
    {
        var configuration = new AchievementConfiguration();
        var snapshot = configuration.Snapshot();
        configuration.Achievements.Clear();
        configuration.Levels.Clear();
        Assert.AreEqual(3, snapshot.Achievements.Count);
        Assert.AreEqual(5, snapshot.Xp.Levels.Count);
        Assert.IsNotEmpty(AchievementConfiguration.Validate(configuration));
        configuration = new AchievementConfiguration { CheckpointSeconds = 1 };
        Assert.IsNotEmpty(AchievementConfiguration.Validate(configuration));
        configuration = new AchievementConfiguration();
        configuration.Achievements.Add(configuration.Achievements[0]);
        Assert.IsNotEmpty(AchievementConfiguration.Validate(configuration));
    }

    [TestMethod]
    public void Module_RejectsForgedInvalidCatalogBeforeRegistration()
    {
        var configuration = new AchievementConfiguration().Snapshot();
        var commands = new CommandRegistry(new AllowAll());
        Assert.ThrowsExactly<ArgumentException>(() => new AchievementModule(
            configuration with { CheckpointSeconds = 0 }, new PlayerRegistry(new AnoEventBus()),
            new Stats(), new Unlocks(), new Grants(), commands));
        Assert.IsEmpty(commands.GetCommands());
    }

    [TestMethod]
    public async Task Commands_DisplayOwnStateAndRemoveRegistrationsOnDispose()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var commands = new CommandRegistry(new AllowAll());
        var stats = new Stats { Totals = [new(GameplayStatKind.HeadshotKill, 10)] };
        var achievements = new Unlocks();
        using var module = new AchievementModule(new AchievementConfiguration().Snapshot(), players,
            stats, achievements, new Grants(), commands);
        var level = await commands.ExecuteAsync("!anolevel", Player);
        Assert.IsTrue(level.Success);
        StringAssert.Contains(level.Message!, "Level 2");
        var status = await commands.ExecuteAsync("!anoachievements", Player);
        Assert.IsTrue(status.Success);
        StringAssert.Contains(status.Message!, "Headshots");
        Assert.AreEqual(CommandFailureReason.Forbidden, (await commands.ExecuteAsync("!anolevel", null)).FailureReason);
        Assert.AreEqual(CommandFailureReason.InvalidInput, (await commands.ExecuteAsync("!anoachievements 0", Player)).FailureReason);
        module.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound, (await commands.ExecuteAsync("!anolevel", Player)).FailureReason);
    }

    [TestMethod]
    public async Task Checkpoints_SkipUnchangedTotalsAndRetryAfterFailure()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var unlocks = new Unlocks();
        var stats = new Stats { Totals = [new(GameplayStatKind.HeadshotKill, 10)] };
        using var module = new AchievementModule(new AchievementConfiguration().Snapshot(), players,
            stats, unlocks, new Grants(), new CommandRegistry(new AllowAll()));
        await module.ReconcileOnlineAsync(Now);
        Assert.AreEqual(3, unlocks.Calls);
        await module.ReconcileOnlineAsync(Now);
        Assert.AreEqual(3, unlocks.Calls);
        stats.Totals = [new(GameplayStatKind.HeadshotKill, 11)];
        unlocks.Fail = true;
        await module.ReconcileOnlineAsync(Now);
        Assert.AreEqual(4, unlocks.Calls);
        unlocks.Fail = false;
        await module.ReconcileOnlineAsync(Now);
        Assert.AreEqual(7, unlocks.Calls);
        await players.DisconnectAsync(Player, players.OnlinePlayers.Single().SessionId, Now.AddMinutes(1));
        await Connect(players);
        await module.ReconcileOnlineAsync(Now);
        Assert.AreEqual(10, unlocks.Calls);
    }

    [TestMethod]
    public async Task RegistrationCollision_RollsBackOwnedCommands()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var commands = new CommandRegistry(new AllowAll());
        using var reserved = commands.Register(new ModuleId("reserved"),
            new CommandDescriptor("anoachievements", "Reserved"), _ => ValueTask.FromResult(CommandResult.Ok()));
        Assert.ThrowsExactly<InvalidOperationException>(() => new AchievementModule(
            new AchievementConfiguration().Snapshot(), players, new Stats(), new Unlocks(), new Grants(), commands));
        Assert.AreEqual(CommandFailureReason.NotFound, (await commands.ExecuteAsync("!anolevel", Player)).FailureReason);
    }

    [TestMethod]
    public async Task ReconnectDuringCommandRead_SuppressesStaleOutput()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var commands = new CommandRegistry(new AllowAll());
        var stats = new Stats
        {
            BeforeRead = async () =>
            {
                await players.DisconnectAsync(Player, players.OnlinePlayers.Single().SessionId, Now);
                await Connect(players);
            },
        };
        using var module = new AchievementModule(new AchievementConfiguration().Snapshot(), players,
            stats, new Unlocks(), new Grants(), commands);
        var result = await commands.ExecuteAsync("!anoachievements", Player);
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Message);
    }

    [TestMethod]
    public async Task Checkpoints_IsolateFailingPlayers()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var other = new PlayerId(Player.SteamId64 + 1);
        await players.ConnectAsync(new PlayerConnection(other, "Other", PlayerTeam.CounterTerrorist, true, Now));
        var unlocks = new Unlocks { FailPlayer = Player };
        using var module = new AchievementModule(new AchievementConfiguration().Snapshot(), players,
            new Stats(), unlocks, new Grants(), new CommandRegistry(new AllowAll()));
        await module.ReconcileOnlineAsync(Now);
        Assert.AreEqual(4, unlocks.Calls);
        unlocks.FailPlayer = null;
        await module.ReconcileOnlineAsync(Now);
        Assert.AreEqual(7, unlocks.Calls);
    }

    [TestMethod]
    public async Task ConcurrentCheckpoint_IsSkippedAndUnloadSuppressesInFlightUnlocks()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stats = new Stats { BeforeRead = async () => { started.SetResult(); await release.Task; } };
        var unlocks = new Unlocks();
        using var module = new AchievementModule(new AchievementConfiguration().Snapshot(), players,
            stats, unlocks, new Grants(), new CommandRegistry(new AllowAll()));
        var first = module.ReconcileOnlineAsync(Now).AsTask();
        await started.Task;
        await module.ReconcileOnlineAsync(Now);
        module.Dispose();
        release.SetResult();
        await first;
        Assert.AreEqual(0, unlocks.Calls);
        await module.ReconcileOnlineAsync(Now);
        Assert.AreEqual(0, unlocks.Calls);
    }

    private static async Task Connect(PlayerRegistry players)
        => _ = await players.ConnectAsync(new PlayerConnection(Player, "Player", PlayerTeam.Terrorist, true, Now));

    private sealed class Stats : IGameplayStatRepository
    {
        public IReadOnlyList<GameplayStatTotal> Totals { get; set; } = [];
        public Func<Task>? BeforeRead { get; init; }
        public ValueTask RecordAsync(GameplayStatEvent statistic, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Achievements must not create counters.");
        public async ValueTask<IReadOnlyList<GameplayStatTotal>> ReadAsync(PlayerId playerId,
            GameplayStatFilter? filter = null, CancellationToken cancellationToken = default)
        {
            if (BeforeRead is not null) await BeforeRead();
            return Totals;
        }
    }

    private sealed class Unlocks : IAchievementRepository
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public bool AwardFirst { get; init; }
        public PlayerId? FailPlayer { get; set; }
        public ValueTask<int> ReadAwardedTierAsync(PlayerId playerId, string achievementId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0);
        public ValueTask<IReadOnlyList<AchievementUnlockRecord>> UnlockAsync(PlayerId playerId,
            AchievementDefinition definition, IEnumerable<GameplayStatTotal> totals, DateTimeOffset occurredAt,
            ProgressionDefinitionSnapshot xpDefinitions, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fail || playerId == FailPlayer) throw new InvalidOperationException("test");
            if (AwardFirst && Calls == 1)
                return ValueTask.FromResult<IReadOnlyList<AchievementUnlockRecord>>([new(definition.Id, definition.Version, 1,
                    new ProgressionGrantRecord(playerId, "achievement:headshots:1", ProgressionXpSource.AchievementReward,
                        100, 100, "achievement.headshots", occurredAt, null, 1, 100, 1))]);
            return ValueTask.FromResult<IReadOnlyList<AchievementUnlockRecord>>([]);
        }
    }

    private sealed class Grants : IProgressionGrantRepository
    {
        public ValueTask<ProgressionLifetimeState> ReadLifetimeAsync(PlayerId playerId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ProgressionLifetimeState(playerId, 100, 1));
        public ValueTask<ProgressionGrantRecord?> ReadGrantAsync(PlayerId playerId, string grantId, CancellationToken cancellationToken = default)
            => throw new AssertFailedException();
        public ValueTask<ProgressionGrantCommitResult> ApplyAsync(PlayerId playerId, ProgressionGrantCandidate candidate, CancellationToken cancellationToken = default)
            => throw new AssertFailedException();
    }

    private sealed class AllowAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class Settings : IPlayerSettingsService
    {
        public ValueTask<T> GetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(key.DefaultValue);
        public ValueTask SetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, T value, CancellationToken cancellationToken = default)
            => throw new AssertFailedException();
        public ValueTask<bool> ResetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, CancellationToken cancellationToken = default)
            => throw new AssertFailedException();
    }

    private sealed class Messages : IMessageService
    {
        public List<MessageRequest> Requests { get; } = [];
        public ValueTask<MessageDispatchResult> SendAsync(MessageRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(MessageDispatchResult.DeliveredResult);
        }
    }
}
