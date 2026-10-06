using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Progression;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class ChallengeModuleTests
{
    private static readonly PlayerId Player = new(76561198000260101);
    private static readonly DateTimeOffset Monday = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
    private static ProgressionDefinitionSnapshot Xp => ProgressionDefinitionSnapshot.Create([new(1, 0)], []);

    [TestMethod]
    public void Configuration_ResolvesUtcWeeksAndSnapshotsMutableInputs()
    {
        var configuration = new ChallengeConfiguration();
        var snapshot = configuration.Snapshot();
        configuration.Recurring.Clear();
        var sunday = snapshot.ResolveAt(Monday.AddDays(7).AddTicks(-10));
        var next = snapshot.ResolveAt(Monday.AddDays(7));
        Assert.AreEqual(3, sunday.Challenges.Count);
        Assert.AreEqual(Monday, sunday.Challenges[0].StartsAtUtc);
        Assert.AreEqual(Monday.AddDays(7), next.Challenges[0].StartsAtUtc);
        Assert.AreEqual(next.Challenges[0].Id, sunday.Challenges[0].Id);
    }

    [TestMethod]
    public void Configuration_ValidatesBoundsAndPrerequisitesAndDailyUtcOffsets()
    {
        var configuration = new ChallengeConfiguration();
        configuration.Recurring[0] = configuration.Recurring[0] with { WindowKind = ChallengeWindowKind.Daily };
        var resolved = configuration.Snapshot().ResolveAt(Monday.AddHours(23).ToOffset(TimeSpan.FromHours(2)));
        Assert.AreEqual(Monday, resolved.Get(configuration.Recurring[0].Id).StartsAtUtc);
        Assert.AreEqual(Monday.AddDays(1), resolved.Get(configuration.Recurring[0].Id).EndsAtUtc);
        configuration.Recurring[0] = configuration.Recurring[0] with { PrerequisiteIds = ["missing"] };
        Assert.IsNotEmpty(ChallengeConfiguration.Validate(configuration));
        configuration = new ChallengeConfiguration { CheckpointSeconds = 1 };
        Assert.IsNotEmpty(ChallengeConfiguration.Validate(configuration));
        configuration = new ChallengeConfiguration();
        configuration.Predefined.Add(new("season", 1, "Season", ChallengeWindowKind.Season,
            AnoCore.Abstractions.Stats.GameplayStatKind.RoundWon, 100, 1000, Monday, Monday.AddDays(90), []));
        Assert.AreEqual(4, configuration.Snapshot().ResolveAt(Monday).Challenges.Count);
    }

    [TestMethod]
    public void Configuration_SnapshotsPrerequisitesAndRejectsNullOrOversizedLists()
    {
        var configuration = new ChallengeConfiguration();
        var prerequisites = new List<string> { configuration.Recurring[2].Id };
        configuration.Recurring[0] = configuration.Recurring[0] with { PrerequisiteIds = prerequisites };
        var snapshot = configuration.Snapshot();
        prerequisites.Clear();
        Assert.HasCount(1, snapshot.ResolveAt(Monday).Get(configuration.Recurring[0].Id).PrerequisiteIds);
        configuration.Recurring[0] = configuration.Recurring[0] with
        {
            PrerequisiteIds = Enumerable.Range(0, 33).Select(index => $"dependency.{index}").ToArray(),
        };
        Assert.IsNotEmpty(ChallengeConfiguration.Validate(configuration));
        Assert.IsNotEmpty(ChallengeConfiguration.Validate(null!));
    }

    [TestMethod]
    public async Task Command_ShowsOwnProgressAndDisposesRegistration()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var commands = new CommandRegistry(new AllowAll());
        using var module = new ChallengeModule(new ChallengeConfiguration().Snapshot(), Xp, players,
            new Repository(), commands, () => Monday);
        var result = await commands.ExecuteAsync("!anochallenges", Player);
        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.Message!, "Challenges 1/1");
        StringAssert.Contains(result.Message!, "0/5");
        Assert.AreEqual(CommandFailureReason.Forbidden, (await commands.ExecuteAsync("!anochallenges", null)).FailureReason);
        Assert.AreEqual(CommandFailureReason.InvalidInput, (await commands.ExecuteAsync("!anochallenges 0", Player)).FailureReason);
        module.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound, (await commands.ExecuteAsync("!anochallenges", Player)).FailureReason);
    }

    [TestMethod]
    public async Task ReconnectDuringRead_SuppressesStaleCommandOutput()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var commands = new CommandRegistry(new AllowAll());
        var repository = new Repository { BeforeRead = async () => await Connect(players) };
        using var module = new ChallengeModule(new ChallengeConfiguration().Snapshot(), Xp, players,
            repository, commands, () => Monday);
        var result = await commands.ExecuteAsync("!anochallenges", Player);
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Message);
    }

    [TestMethod]
    public async Task Checkpoint_OrdersPrerequisitesAndIsolatesFailures()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var configuration = new ChallengeConfiguration();
        configuration.Recurring[0] = configuration.Recurring[0] with { PrerequisiteIds = [configuration.Recurring[2].Id] };
        var repository = new Repository { FailId = configuration.Recurring[2].Id };
        using var module = new ChallengeModule(configuration.Snapshot(), Xp, players, repository,
            new CommandRegistry(new AllowAll()));
        await module.ReconcileOnlineAsync(Monday);
        Assert.HasCount(3, repository.Completed);
        Assert.IsTrue(repository.Completed.IndexOf(configuration.Recurring[2].Id)
            < repository.Completed.IndexOf(configuration.Recurring[0].Id));
        repository.FailId = null;
        await module.ReconcileOnlineAsync(Monday);
        Assert.HasCount(6, repository.Completed);
    }

    [TestMethod]
    public async Task ConcurrentCheckpoint_IsSkippedAndUnloadStopsLaterWork()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Repository { BeforeComplete = async () => { started.TrySetResult(); await release.Task; } };
        using var module = new ChallengeModule(new ChallengeConfiguration().Snapshot(), Xp, players,
            repository, new CommandRegistry(new AllowAll()));
        var first = module.ReconcileOnlineAsync(Monday).AsTask();
        await started.Task;
        await module.ReconcileOnlineAsync(Monday);
        module.Dispose();
        release.SetResult();
        await first;
        Assert.HasCount(1, repository.Completed);
    }

    [TestMethod]
    public void RegistrationCollision_DoesNotRemoveExistingCommand()
    {
        var commands = new CommandRegistry(new AllowAll());
        using var reserved = commands.Register(new ModuleId("reserved"), new CommandDescriptor("anochallenges", "Reserved"),
            _ => ValueTask.FromResult(CommandResult.Ok("reserved")));
        Assert.ThrowsExactly<InvalidOperationException>(() => new ChallengeModule(new ChallengeConfiguration().Snapshot(), Xp,
            new PlayerRegistry(new AnoEventBus()), new Repository(), commands));
        Assert.HasCount(1, commands.GetCommands());
    }

    private static async Task Connect(PlayerRegistry players)
        => _ = await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Monday));

    private sealed class Repository : IChallengeRepository
    {
        public Func<Task>? BeforeRead { get; init; }
        public Func<Task>? BeforeComplete { get; init; }
        public string? FailId { get; set; }
        public List<string> Completed { get; } = [];
        public async ValueTask<ChallengeEvaluation> ReadAsync(PlayerId playerId, ChallengeCatalogSnapshot catalog,
            string challengeId, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            if (BeforeRead is not null) await BeforeRead();
            return catalog.Evaluate(challengeId, [], [], at);
        }
        public async ValueTask<ChallengeCompletionResult> CompleteAsync(PlayerId playerId, ChallengeCatalogSnapshot catalog,
            string challengeId, DateTimeOffset at, ProgressionDefinitionSnapshot xpDefinitions, CancellationToken cancellationToken = default)
        {
            Completed.Add(challengeId);
            if (BeforeComplete is not null) await BeforeComplete();
            if (challengeId == FailId) throw new InvalidOperationException("test failure");
            return new(false, catalog.Evaluate(challengeId, [], [], at), null);
        }
    }

    private sealed class AllowAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }
}
