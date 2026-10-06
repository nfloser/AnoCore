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
public sealed class GameplayStatsModuleTests
{
    private static readonly PlayerId Player = new(76561198000184021);
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 17, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Composition_OwnsRatingRegistrationAndRollsBackOnCollision()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var commands = new CommandRegistry(new AllowAll());
        var module = new GameplayStatsModule(commands, players, new FakeRepository(),
            combat: new FakeCombatRepository());
        Assert.IsTrue((await commands.ExecuteAsync("!anorating", null)).Success);
        module.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anorating", null)).FailureReason);
        using var reserved = commands.Register(new AnoCore.Abstractions.Modules.ModuleId("reserved"),
            new CommandDescriptor("anorating", "Reserved."),
            _ => ValueTask.FromResult(CommandResult.Ok("reserved")));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new GameplayStatsModule(commands, players, new FakeRepository(),
                combat: new FakeCombatRepository()));
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anogamestats", null)).FailureReason);
        Assert.AreEqual("reserved", (await commands.ExecuteAsync("!anorating", null)).Message);
    }

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
    public async Task Menu_CombinesRepositoriesFiltersNavigatesAndCleansReconnect()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new AllowAll());
        var menus = new MenuService();
        var gameplay = new FakeRepository
        {
            Totals =
            [
                new(GameplayStatKind.GrenadeThrown, 3),
                new(GameplayStatKind.BombPlanted, 2),
                new(GameplayStatKind.Mvp, 1),
            ],
        };
        var combat = new FakeCombatRepository
        {
            Totals = new CombatTotals(10, 4, 5),
            Details = new CombatDetailTotals(40, 20, 700, 90, 6),
            Hitgroups =
            [
                new CombatHitgroupTotals(1, 6, 300, 20),
                new CombatHitgroupTotals(2, 14, 400, 70),
            ],
        };
        using var module = new GameplayStatsModule(
            commands, players, gameplay, GameplayStatsConfiguration.Default,
            combat, menus, events);

        var result = await commands.ExecuteAsync(
            "!anostatsmenu de_dust2 weapon_ak47", Player);
        Assert.IsTrue(result.Success);
        Assert.AreEqual("de_dust2", gameplay.LastFilter?.MapName);
        Assert.AreEqual("de_dust2", combat.LastDetailFilter?.MapName);
        Assert.AreEqual("weapon_ak47", combat.LastDetailFilter?.Weapon);

        Assert.IsTrue(menus.TryGetOpenMenu(Player, out var first));
        Assert.IsNotNull(first);
        Assert.IsTrue(first.Options.Any(x => x.Label == "K/D/A: 10/4/5"));
        var staleData = first.Options.First(x => x.Id.StartsWith("s", StringComparison.Ordinal));
        var next = first.Options.Single(x => x.Label == "Next page");
        Assert.IsTrue((await menus.SelectAsync(Player, next.Id)).Accepted);

        Assert.IsTrue(menus.TryGetOpenMenu(Player, out var second));
        Assert.IsNotNull(second);
        StringAssert.Contains(second.Title, "page 2");
        Assert.IsFalse((await menus.SelectAsync(Player, staleData.Id)).Accepted);

        await players.ConnectAsync(new PlayerConnection(
            Player, "Replacement", PlayerTeam.CounterTerrorist, true, Now.AddSeconds(1)));
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));

        module.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anostatsmenu", Player)).FailureReason);
    }

    [TestMethod]
    public async Task Menu_DiscardsInFlightCombatReadAfterReconnect()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new AllowAll());
        var menus = new MenuService();
        var combat = new FakeCombatRepository
        {
            ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseRead = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        using var module = new GameplayStatsModule(
            commands, players, new FakeRepository(), GameplayStatsConfiguration.Default,
            combat, menus, events);

        var pending = commands.ExecuteAsync("!anostatsmenu", Player).AsTask();
        await combat.ReadStarted.Task;
        await players.ConnectAsync(new PlayerConnection(
            Player, "Replacement", PlayerTeam.CounterTerrorist, true, Now.AddSeconds(1)));
        combat.ReleaseRead.TrySetResult(true);

        var result = await pending;
        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, result.FailureReason);
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
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

    private sealed class FakeCombatRepository : ICombatDetailRepository
    {
        public CombatTotals Totals { get; set; } = new(0, 0, 0);
        public CombatDetailTotals Details { get; set; } = new(0, 0, 0, 0, 0);
        public IReadOnlyList<CombatHitgroupTotals> Hitgroups { get; set; } = [];
        public CombatDetailFilter? LastDetailFilter { get; private set; }
        public TaskCompletionSource<bool>? ReadStarted { get; set; }
        public TaskCompletionSource<bool>? ReleaseRead { get; set; }

        public ValueTask RecordAsync(CombatDeath death,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public async ValueTask<CombatTotals> ReadAsync(PlayerId playerId,
            CancellationToken cancellationToken = default)
        {
            ReadStarted?.TrySetResult(true);
            if (ReleaseRead is not null)
                await ReleaseRead.Task.WaitAsync(cancellationToken);
            return Totals;
        }

        public ValueTask<IReadOnlyList<CombatRankEntry>> GetTopKillsAsync(
            int limit, int offset, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatRankEntry>>([]);

        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopDeathsAsync(
            int limit, int offset, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatCountRankEntry>>([]);

        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopAssistsAsync(
            int limit, int offset, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatCountRankEntry>>([]);

        public ValueTask RecordWeaponFireAsync(CombatWeaponFireEvent weaponFire,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask RecordDamageAsync(CombatDamageEvent damage,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<CombatDetailTotals> ReadDetailsAsync(
            PlayerId playerId, CombatDetailFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            LastDetailFilter = filter;
            return ValueTask.FromResult(Details);
        }

        public ValueTask<IReadOnlyList<CombatHitgroupTotals>> ReadHitgroupsAsync(
            PlayerId playerId, CombatDetailFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            LastDetailFilter = filter;
            return ValueTask.FromResult(Hitgroups);
        }
    }

    private sealed class AllowAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
}
