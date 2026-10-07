using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class RankScoreboardTests
{
    [TestMethod]
    public void ScoreboardMapping_ClampsNativeScoresAndMapsExplicitModes()
    {
        var config = new RankConfiguration { Scoreboard = new() { SyncScore = true, RankMode = RankScoreboardMode.Premier } };
        var value = RankScoreboardProjection.Create(config, long.MaxValue);
        Assert.AreEqual(int.MaxValue, value.Score);
        Assert.AreEqual(int.MaxValue, value.Badge!.Ranking);
        Assert.AreEqual((sbyte)11, value.Badge.Type);
        config.Scoreboard.RankMode = RankScoreboardMode.Competitive;
        value = RankScoreboardProjection.Create(config, 100);
        Assert.AreEqual(3, value.Badge!.Ranking);
        Assert.AreEqual((sbyte)12, value.Badge.Type);
        config.Scoreboard.RankMode = RankScoreboardMode.Wingman;
        Assert.AreEqual((sbyte)7, RankScoreboardProjection.Create(config, 100).Badge!.Type);
        config.Scoreboard.RankMode = RankScoreboardMode.DangerZone;
        Assert.AreEqual((sbyte)10, RankScoreboardProjection.Create(config, 100).Badge!.Type);
        config.Scoreboard = new();
        value = RankScoreboardProjection.Create(config, 100);
        Assert.IsNull(value.Score);
        Assert.IsNull(value.Badge);
    }

    [TestMethod]
    public void NativeOwnership_RestoresOriginalOnlyWhileValuesRemainOwned()
    {
        var owned = new OwnedRankScoreboardValue<int>();
        Assert.IsTrue(owned.TryApply(7, 10));
        Assert.IsTrue(owned.TryApply(10, 20));
        Assert.IsTrue(owned.TryRestore(20, out var original));
        Assert.AreEqual(7, original);
        var changed = new OwnedRankScoreboardValue<int>();
        Assert.IsTrue(changed.TryApply(7, 10));
        Assert.IsFalse(changed.TryApply(99, 20));
        Assert.IsFalse(changed.TryRestore(99, out _));
        Assert.IsFalse(changed.TryApply(99, 30));
        var score = new OwnedRankScoreboardValue<int>();
        Assert.IsTrue(score.TryApply(7, 10, replaceExternal: true));
        Assert.IsTrue(score.TryApply(11, 20, replaceExternal: true));
        Assert.IsTrue(score.TryRestore(20, out var latestExternal));
        Assert.AreEqual(11, latestExternal);
    }

    [TestMethod]
    public void Configuration_RejectsUnknownModesAndKeepsUpgradeDefaultsDisabled()
    {
        Assert.IsFalse(new RankConfiguration().Scoreboard.Enabled);
        Assert.IsNotEmpty(RankConfiguration.Validate(new RankConfiguration { Scoreboard = null! }));
        Assert.IsNotEmpty(RankConfiguration.Validate(new RankConfiguration { Scoreboard = new() { RankMode = (RankScoreboardMode)255 } }));
    }
    [TestMethod]
    public async Task Refresh_SuppressesReconnectDuringQueryAndOwnsTransportDisposal()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var id = new PlayerId(76561198000286101);
        var now = DateTimeOffset.UtcNow;
        await players.ConnectAsync(new PlayerConnection(id, "Old", PlayerTeam.Terrorist, true, now));
        players.TryGet(id, out var player);
        var scores = new Scores
        {
            BeforeRead = async () =>
            {
                await players.DisconnectAsync(id, player!.SessionId, now);
                await players.ConnectAsync(new PlayerConnection(id, "New", PlayerTeam.Terrorist, true, now));
            },
        };
        var transport = new Transport();
        using var service = new RankScoreboardService(new RankConfiguration
        {
            Source = RankScoreSource.EventLedger,
            Scoreboard = new() { SyncScore = true },
        }, players, scores, transport);
        await service.RefreshAsync();
        Assert.AreEqual(RankScoreSource.EventLedger, scores.Source);
        Assert.IsEmpty(transport.Players);
        service.Dispose();
        Assert.IsTrue(transport.Disposed);
        await service.RefreshAsync();
        Assert.IsEmpty(transport.Players);
    }

    [TestMethod]
    public async Task Refresh_IsolatesPlayerFailureAndCapturesConfiguration()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var now = DateTimeOffset.UtcNow;
        await players.ConnectAsync(new PlayerConnection(new PlayerId(76561198000286101), "One", PlayerTeam.Terrorist, true, now));
        await players.ConnectAsync(new PlayerConnection(new PlayerId(76561198000286102), "Two", PlayerTeam.Terrorist, true, now));
        var config = new RankConfiguration { Scoreboard = new() { SyncScore = true } };
        var transport = new Transport { FailFirst = true };
        var errors = new List<Exception>();
        using var service = new RankScoreboardService(config, players, new Scores(), transport, errors.Add);
        config.Scoreboard.SyncScore = false;
        await service.RefreshAsync();
        Assert.HasCount(1, errors);
        Assert.HasCount(1, transport.Players);
        Assert.AreEqual(100, transport.Last!.Score);
    }

    private sealed class Transport : IRankScoreboardTransport
    {
        public List<PlayerSnapshot> Players { get; } = [];
        public RankScoreboardProjection? Last { get; private set; }
        public bool FailFirst { get; set; }
        public bool Disposed { get; private set; }
        public ValueTask ApplyAsync(PlayerSnapshot player, RankScoreboardProjection projection, CancellationToken cancellationToken = default)
        {
            if (FailFirst) { FailFirst = false; throw new InvalidOperationException("test failure"); }
            Players.Add(player);
            Last = projection;
            return ValueTask.CompletedTask;
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class Scores : IGameplayRankScoreRepository
    {
        public Func<Task>? BeforeRead { get; init; }
        public RankScoreSource Source { get; private set; }
        public async ValueTask<CombatScoreRankEntry?> GetScorePlacementAsync(PlayerId id, RankScoreWeights weights, CancellationToken cancellationToken = default)
        {
            Source = weights.Source;
            if (BeforeRead is not null) await BeforeRead();
            return new(id, 100, 1);
        }
        public ValueTask<IReadOnlyList<CombatScoreRankEntry>> GetTopScoresAsync(RankScoreWeights weights, int limit, int offset, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public ValueTask<long> ReadRawScoreAsync(PlayerId id, RankScoreWeights weights, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public ValueTask RecordAsync(CombatDeath death, CancellationToken cancellationToken = default) => throw new AssertFailedException("Rank events must not write combat statistics.");
        public ValueTask<CombatTotals> ReadAsync(PlayerId id, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public ValueTask<IReadOnlyList<CombatRankEntry>> GetTopKillsAsync(int limit, int offset, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopDeathsAsync(int limit, int offset, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopAssistsAsync(int limit, int offset, CancellationToken cancellationToken = default) => throw new AssertFailedException();
    }
}
