using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class AnoRatingModuleTests
{
    private static readonly PlayerId Alice = new(76561198000019901);
    private static readonly PlayerId Bob = new(76561198000019902);
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Commands_ListSortTargetAndDisposeWithoutRankQueriesOrWrites()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await players.ConnectAsync(new PlayerConnection(Bob, "Bob", PlayerTeam.Terrorist, true, Now));
        await players.ConnectAsync(new PlayerConnection(Alice, "Alice", PlayerTeam.CounterTerrorist, true, Now));
        var commands = new CommandRegistry(new AllowAll());
        var repository = new Repository();
        repository.Totals[Bob] = new(100, 10, 20);
        repository.Totals[Alice] = new(50, 100, 0);
        var module = new AnoRatingModule(commands, players, repository, repository);
        var list = await commands.ExecuteAsync("!anorating", Alice);
        Assert.IsTrue(list.Success);
        Assert.IsTrue(list.Message!.IndexOf("Bob:", StringComparison.Ordinal)
            < list.Message.IndexOf("Alice:", StringComparison.Ordinal));
        var detail = await commands.ExecuteAsync("!anorating Alice", null);
        Assert.IsTrue(detail.Success);
        StringAssert.Contains(detail.Message!, "combat=");
        StringAssert.Contains(detail.Message!, "100 rounds");
        StringAssert.Contains(detail.Message!, AnoRatingCalculator.Version);
        Assert.IsTrue((await commands.ExecuteAsync($"!anorating {Bob.SteamId64}", Alice)).Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput,
            (await commands.ExecuteAsync("!anorating absent", Alice)).FailureReason);
        Assert.AreEqual(CommandFailureReason.InvalidInput,
            (await commands.ExecuteAsync("!anorating list 27", Alice)).FailureReason);
        module.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anorating", Alice)).FailureReason);
    }

    [TestMethod]
    public async Task LowSamples_AmbiguousNamesAndDeterministicPagination()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        for (var index = 0; index < 7; index++)
            await players.ConnectAsync(new PlayerConnection(new PlayerId(Alice.SteamId64 + (ulong)index),
                $"Player{index}", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new AllowAll());
        var repository = new Repository { Rounds = 0 };
        using var module = new AnoRatingModule(commands, players, repository, repository);
        var first = await commands.ExecuteAsync("!anorating", null);
        StringAssert.Contains(first.Message!, "1/2");
        StringAssert.Contains(first.Message!, "unscored (provisional)");
        Assert.IsFalse(first.Message!.Contains("Player5", StringComparison.Ordinal));
        var second = await commands.ExecuteAsync("!anorating list 2", null);
        StringAssert.Contains(second.Message!, "Player5");
        StringAssert.Contains(second.Message!, "Player6");
        Assert.AreEqual(CommandFailureReason.InvalidInput,
            (await commands.ExecuteAsync("!anorating Player", null)).FailureReason);
        Assert.IsTrue((await commands.ExecuteAsync("!anorating Player0", null)).Success);
    }

    [TestMethod]
    public async Task CallerReconnect_RejectsInFlightResult()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var original = await players.ConnectAsync(new PlayerConnection(Alice, "Alice",
            PlayerTeam.Terrorist, true, Now));
        await players.ConnectAsync(new PlayerConnection(Bob, "Bob", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new AllowAll());
        var repository = new Repository
        {
            BeforeRead = async () =>
            {
                await players.DisconnectAsync(Alice, original.SessionId, Now.AddSeconds(1));
                await players.ConnectAsync(new PlayerConnection(Alice, "Replacement",
                    PlayerTeam.Terrorist, true, Now.AddSeconds(2)));
            },
        };
        using var module = new AnoRatingModule(commands, players, repository, repository);
        var result = await commands.ExecuteAsync("!anorating Bob", Alice);
        Assert.AreEqual(CommandFailureReason.InvalidInput, result.FailureReason);
        StringAssert.Contains(result.Message!, "session changed");
        Assert.AreEqual(0, repository.GameplayReads);
    }

    [TestMethod]
    public async Task TargetReconnect_RejectsOldDetailEvenForConsole()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var original = await players.ConnectAsync(new PlayerConnection(Bob, "Bob",
            PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new AllowAll());
        var repository = new Repository
        {
            BeforeRead = async () =>
            {
                await players.DisconnectAsync(Bob, original.SessionId, Now.AddSeconds(1));
                await players.ConnectAsync(new PlayerConnection(Bob, "NewBob",
                    PlayerTeam.Terrorist, true, Now.AddSeconds(2)));
            },
        };
        using var module = new AnoRatingModule(commands, players, repository, repository);
        var result = await commands.ExecuteAsync("!anorating Bob", null);
        Assert.AreEqual(CommandFailureReason.InvalidInput, result.FailureReason);
        Assert.AreEqual(0, repository.GameplayReads);
    }

    private sealed class AllowAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class Repository : ICombatRepository, IGameplayStatRepository
    {
        public Dictionary<PlayerId, CombatTotals> Totals { get; } = [];
        public long Rounds { get; set; } = 100;
        public Func<ValueTask>? BeforeRead { get; set; }
        public int GameplayReads { get; private set; }
        public ValueTask RecordAsync(CombatDeath death, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Rating must never write combat events.");
        public ValueTask RecordAsync(GameplayStatEvent statistic, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Rating must never write gameplay events.");
        public async ValueTask<CombatTotals> ReadAsync(PlayerId playerId,
            CancellationToken cancellationToken = default)
        {
            if (BeforeRead is not null) await BeforeRead();
            return Totals.GetValueOrDefault(playerId) ?? new CombatTotals(0, 0, 0);
        }
        public ValueTask<IReadOnlyList<GameplayStatTotal>> ReadAsync(PlayerId playerId,
            GameplayStatFilter? filter = null, CancellationToken cancellationToken = default)
        {
            GameplayReads++;
            return ValueTask.FromResult<IReadOnlyList<GameplayStatTotal>>(
                [new(GameplayStatKind.RoundPlayed, Rounds)]);
        }
        public ValueTask<IReadOnlyList<CombatRankEntry>> GetTopKillsAsync(int limit, int offset,
            CancellationToken cancellationToken = default) => throw new AssertFailedException("Unexpected leaderboard query.");
        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopDeathsAsync(int limit, int offset,
            CancellationToken cancellationToken = default) => throw new AssertFailedException("Unexpected leaderboard query.");
        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopAssistsAsync(int limit, int offset,
            CancellationToken cancellationToken = default) => throw new AssertFailedException("Unexpected leaderboard query.");
        public ValueTask<CombatScoreRankEntry?> GetScorePlacementAsync(PlayerId playerId,
            int killPoints, int assistPoints, int deathPenalty, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Rating must not read rank points.");
    }
}
