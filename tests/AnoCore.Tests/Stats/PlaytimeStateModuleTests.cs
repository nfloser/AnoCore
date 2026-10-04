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
public sealed class PlaytimeStateModuleTests
{
    private static readonly PlayerId Player = new(76561198000011311);
    private static readonly DateTimeOffset Start = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task PlayerUpdates_CheckpointPreviousStateAndCarryCurrentStateForward()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var repository = new StateRepository();
        var commands = new CommandRegistry(new AllowAll());
        using var module = await PlaytimeModule.CreateAsync(events, players, repository, commands);

        var current = await players.ConnectAsync(
            new PlayerConnection(Player, "A", PlayerTeam.Terrorist, true, Start));
        await players.UpdateAsync(new PlayerStateUpdate(Player, current.SessionId, null,
            PlayerTeam.CounterTerrorist, null, Start.AddSeconds(10)));
        await players.UpdateAsync(new PlayerStateUpdate(Player, current.SessionId, null,
            null, false, Start.AddSeconds(20)));
        await module.CheckpointOnlineAsync(Start.AddSeconds(30));
        await players.DisconnectAsync(Player, current.SessionId, Start.AddSeconds(40));

        CollectionAssert.AreEqual(new[]
        {
            (Start.AddSeconds(10), new PlaytimeState(PlayerTeam.CounterTerrorist, true), false),
            (Start.AddSeconds(20), new PlaytimeState(PlayerTeam.CounterTerrorist, false), false),
            (Start.AddSeconds(30), new PlaytimeState(PlayerTeam.CounterTerrorist, false), false),
            (Start.AddSeconds(40), new PlaytimeState(PlayerTeam.CounterTerrorist, false), true),
        }, repository.StateAdvances.ToArray());
        Assert.AreEqual(0, repository.LegacyAdvanceCalls);
        Assert.AreEqual(1, repository.StateOpenCalls);
    }

    [TestMethod]
    public async Task OwnPlaytime_RendersBoundedStateBreakdown()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var repository = new StateRepository
        {
            Totals = new PlaytimeTotals(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10)),
            Breakdown =
            [
                new(PlayerTeam.Terrorist, true, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(5)),
                new(PlayerTeam.CounterTerrorist, false, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5)),
            ],
        };
        var commands = new CommandRegistry(new AllowAll());
        using var module = await PlaytimeModule.CreateAsync(events, players, repository, commands,
            new FixedTime(Start.AddMinutes(30)));
        await players.ConnectAsync(new PlayerConnection(Player, "A", PlayerTeam.Terrorist, true, Start));

        var result = await commands.ExecuteAsync("!anoplaytime", Player);

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.Message!, "T/alive");
        StringAssert.Contains(result.Message!, "CT/dead");
        Assert.AreEqual(1, repository.BreakdownReads);
    }

    private sealed class AllowAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StateRepository : IPlaytimeStateRepository
    {
        public int LegacyAdvanceCalls { get; private set; }
        public int StateOpenCalls { get; private set; }
        public int BreakdownReads { get; private set; }
        public PlaytimeTotals Totals { get; set; } = new(TimeSpan.Zero, TimeSpan.Zero);
        public IReadOnlyList<PlaytimeStateBreakdown> Breakdown { get; set; } = [];
        public List<(DateTimeOffset At, PlaytimeState State, bool Close)> StateAdvances { get; } = [];

        public ValueTask OpenAsync(PlayerId playerId, PlayerSessionId sessionId,
            DateTimeOffset startedAtUtc, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask AdvanceAsync(PlayerId playerId, PlayerSessionId sessionId,
            DateTimeOffset atUtc, bool close = false, CancellationToken cancellationToken = default)
        {
            LegacyAdvanceCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask OpenStateAsync(PlayerId playerId, PlayerSessionId sessionId,
            DateTimeOffset startedAtUtc, PlaytimeState state,
            CancellationToken cancellationToken = default)
        {
            StateOpenCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask AdvanceStateAsync(PlayerId playerId, PlayerSessionId sessionId,
            DateTimeOffset atUtc, PlaytimeState stateAtUtc, bool close = false,
            CancellationToken cancellationToken = default)
        {
            StateAdvances.Add((atUtc, stateAtUtc, close));
            return ValueTask.CompletedTask;
        }

        public ValueTask<PlaytimeTotals> ReadAsync(PlayerId playerId, DateOnly utcDay,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Totals);

        public ValueTask<IReadOnlyList<PlaytimeStateBreakdown>> ReadStateBreakdownAsync(
            PlayerId playerId, DateOnly utcDay, CancellationToken cancellationToken = default)
        {
            BreakdownReads++;
            return ValueTask.FromResult(Breakdown);
        }

        public ValueTask<IReadOnlyList<PlaytimeRankEntry>> GetTopAsync(int limit, int offset,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<PlaytimeRankEntry>>([]);
    }
}
