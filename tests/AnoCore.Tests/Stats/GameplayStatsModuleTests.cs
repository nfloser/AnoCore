using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class GameplayStatsModuleTests
{
    private static readonly PlayerId Player = new(76561198000184021);
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 17, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Command_ReadsOwnStatsWithOptionalMapAndDisposes()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new AllowAll());
        var repository = new FakeRepository
        {
            Totals =
            [
                new(GameplayStatKind.GrenadeThrown, 3),
                new(GameplayStatKind.BombPlanted, 1),
            ],
        };
        var module = new GameplayStatsModule(commands, players, repository);

        var result = await commands.ExecuteAsync("!anogamestats de_dust2", Player);
        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.Message!, "GrenadeThrown=3");
        StringAssert.Contains(result.Message!, "BombPlanted=1");
        Assert.AreEqual("de_dust2", repository.LastFilter?.MapName);

        module.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anogamestats", Player)).FailureReason);
    }

    [TestMethod]
    public async Task Command_RejectsConsoleAndReconnectRace()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new AllowAll());
        var repository = new FakeRepository
        {
            ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseRead = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        using var module = new GameplayStatsModule(commands, players, repository);

        Assert.AreEqual(CommandFailureReason.InvalidInput,
            (await commands.ExecuteAsync("!anogamestats", null)).FailureReason);

        var pending = commands.ExecuteAsync("!anogamestats", Player).AsTask();
        await repository.ReadStarted.Task;
        await players.ConnectAsync(new PlayerConnection(
            Player, "Replacement", PlayerTeam.CounterTerrorist, true, Now.AddSeconds(1)));
        repository.ReleaseRead.TrySetResult(true);

        var result = await pending;
        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, result.FailureReason);
    }

    [TestMethod]
    public async Task Command_DiscardsInFlightReplyAfterDispose()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new AllowAll());
        var repository = new FakeRepository
        {
            ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseRead = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var module = new GameplayStatsModule(commands, players, repository);

        var pending = commands.ExecuteAsync("!anogamestats", Player).AsTask();
        await repository.ReadStarted.Task;
        module.Dispose();
        repository.ReleaseRead.TrySetResult(true);

        var result = await pending;
        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.NotFound, result.FailureReason);
    }

    [TestMethod]
    public async Task Record_ForwardsTypedEvent()
    {
        var repository = new FakeRepository();
        using var module = new GameplayStatsModule(
            new CommandRegistry(new AllowAll()),
            new PlayerRegistry(new AnoEventBus()),
            repository);
        var value = new GameplayStatEvent(
            Guid.NewGuid(), Player, Now, "de_dust2", GameplayStatKind.Mvp);

        await module.RecordAsync(value);

        Assert.AreSame(value, repository.LastRecorded);
    }

    private sealed class FakeRepository : IGameplayStatRepository
    {
        public GameplayStatEvent? LastRecorded { get; private set; }
        public GameplayStatFilter? LastFilter { get; private set; }
        public IReadOnlyList<GameplayStatTotal> Totals { get; set; } = [];
        public TaskCompletionSource<bool>? ReadStarted { get; set; }
        public TaskCompletionSource<bool>? ReleaseRead { get; set; }

        public ValueTask RecordAsync(GameplayStatEvent statistic,
            CancellationToken cancellationToken = default)
        {
            LastRecorded = statistic;
            return ValueTask.CompletedTask;
        }

        public async ValueTask<IReadOnlyList<GameplayStatTotal>> ReadAsync(
            PlayerId playerId, GameplayStatFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            ReadStarted?.TrySetResult(true);
            if (ReleaseRead is not null)
                await ReleaseRead.Task.WaitAsync(cancellationToken);
            return Totals;
        }
    }

    private sealed class AllowAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
}
