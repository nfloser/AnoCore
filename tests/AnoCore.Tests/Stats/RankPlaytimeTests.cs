using AnoCore.Abstractions.Players;
using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class RankPlaytimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly PlayerId Id = new(76561198000284101);
    private static PlayerSnapshot Player() => new(Id, PlayerSessionId.New(), "Player", true, true, PlayerTeam.Terrorist, Now, Now);
    private static RankLiveContext Context(int seconds, bool warmup = false) => new(Guid.NewGuid(), Now.AddSeconds(seconds), warmup, 4, "round");
    private static LiveRankPolicy Policy(int interval = 10) => new(new RankConfiguration
    {
        LivePolicy = new LiveRankConfiguration { PlaytimeIntervalSeconds = interval },
    });

    [TestMethod]
    public async Task EligibleSamples_AwardAtBoundaryWithoutHistoricalBackfill()
    {
        var calls = new List<RankLiveContext>();
        using var service = new RankPlaytimeService(Policy(), (player, context, token) => { calls.Add(context); return ValueTask.CompletedTask; });
        var player = Player();
        await service.TickAsync([player], Context(100));
        await service.TickAsync([player], Context(105));
        Assert.IsEmpty(calls);
        await service.TickAsync([player], Context(110));
        await service.TickAsync([player], Context(110));
        Assert.AreEqual(1, calls.Count);
        await service.TickAsync([player], Context(115));
        Assert.AreEqual(1, calls.Count);
    }

    [TestMethod]
    public async Task IneligibleSpectatorReconnectAndLongGaps_DoNotGrantCatchup()
    {
        var calls = 0;
        using var service = new RankPlaytimeService(Policy(), (player, context, token) => { calls++; return ValueTask.CompletedTask; });
        var player = Player();
        await service.TickAsync([player], Context(0));
        await service.TickAsync([player], Context(5, true));
        await service.TickAsync([player], Context(10));
        await service.TickAsync([player], Context(100));
        Assert.AreEqual(0, calls);
        var replacement = Player();
        await service.TickAsync([replacement], Context(105));
        await service.TickAsync([new PlayerSnapshot(Id, replacement.SessionId, "Player", true, true, PlayerTeam.Spectator, Now, Now)], Context(110));
        await service.TickAsync([replacement], Context(115));
        Assert.AreEqual(0, calls);
        await service.TickAsync([replacement], Context(120));
        await service.TickAsync([replacement], Context(125));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task FailedAward_RetriesFrozenIdentityAndTimeThenAdvancesOnce()
    {
        var attempts = new List<RankLiveContext>();
        var errors = new List<Exception>();
        using var service = new RankPlaytimeService(Policy(), (player, context, token) =>
        {
            attempts.Add(context);
            if (attempts.Count == 1) throw new InvalidOperationException("retry");
            return ValueTask.CompletedTask;
        }, errors.Add);
        var player = Player();
        await service.TickAsync([player], Context(0));
        await service.TickAsync([player], Context(5));
        await service.TickAsync([player], Context(10));
        await service.TickAsync([player], Context(15));
        Assert.AreEqual(attempts[0], attempts[1]);
        Assert.HasCount(1, errors);
        await service.TickAsync([player], Context(20));
        Assert.AreEqual(3, attempts.Count);
        Assert.AreNotEqual(attempts[1].EventId, attempts[2].EventId);
    }

    [TestMethod]
    public async Task DisabledAndDisposedServices_DoNotInvokeAwards()
    {
        var calls = 0;
        using var service = new RankPlaytimeService(Policy(0), (player, context, token) => { calls++; return ValueTask.CompletedTask; });
        await service.TickAsync([Player()], Context(0));
        await service.TickAsync([Player()], Context(100));
        service.Dispose();
        await service.TickAsync([Player()], Context(200));
        Assert.AreEqual(0, calls);
        Assert.IsNotEmpty(LiveRankConfiguration.Validate(new LiveRankConfiguration { PlaytimeIntervalSeconds = -1 }));
        Assert.IsNotEmpty(LiveRankConfiguration.Validate(new LiveRankConfiguration { PlaytimeIntervalSeconds = 86401 }));
    }
}
