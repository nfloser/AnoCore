using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Modules.Stats;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Runtime.Settings;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class RankPointPresentationTests
{
    private static readonly PlayerId Player = new(76561198000282101);
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CommittedChanges_NotifyOnceAndSummarizeVisibleDeltasOncePerRound()
    {
        var players = await Players();
        players.TryGet(Player, out var player);
        var messages = new Messages();
        var toggles = new PlayerToggleCatalog();
        using var service = new RankPointPresentationService(true, true, players, new Settings(), toggles, messages);
        var first = Change(player!, 0, 10);
        await service.CommittedAsync(first);
        await service.CommittedAsync(first);
        await service.CommittedAsync(Change(player!, 10, 7));
        await service.CompleteRoundAsync("round-1");
        await service.CompleteRoundAsync("round-1");
        Assert.AreEqual(3, messages.Requests.Count);
        StringAssert.Contains(messages.Requests[0].Text, "+10");
        StringAssert.Contains(messages.Requests[1].Text, "-3");
        StringAssert.Contains(messages.Requests[2].Text, "+7");
        Assert.IsTrue(messages.Requests.All(request => request.Target.SessionId == player!.SessionId));
        await service.CommittedAsync(Change(player!, 7, 9));
        await service.CompleteRoundAsync("round-1");
        Assert.AreEqual(4, messages.Requests.Count);
        StringAssert.Contains(messages.Requests[3].Text, "+2");
        service.Dispose();
        Assert.IsEmpty(toggles.GetAll());
    }

    [TestMethod]
    public async Task DisabledSettingsAndServerFlagsSuppressOutputWithoutChangingScoreData()
    {
        var players = await Players();
        players.TryGet(Player, out var player);
        var messages = new Messages();
        using (var service = new RankPointPresentationService(true, true, players, new Settings { Enabled = false }, new PlayerToggleCatalog(), messages))
        {
            await service.CommittedAsync(Change(player!, 0, 5));
            await service.CompleteRoundAsync("round-1");
        }
        using (var service = new RankPointPresentationService(false, false, players, new Settings(), new PlayerToggleCatalog(), messages))
        {
            await service.CommittedAsync(Change(player!, 0, 5));
            await service.CompleteRoundAsync("round-1");
        }
        Assert.IsEmpty(messages.Requests);
    }

    [TestMethod]
    public async Task ReconnectAndDisposeDuringPreferenceReadSuppressStaleMessages()
    {
        var players = await Players();
        players.TryGet(Player, out var player);
        var messages = new Messages();
        var settings = new Settings
        {
            BeforeRead = async () =>
            {
                await players.DisconnectAsync(Player, player!.SessionId, Now);
                await players.ConnectAsync(new PlayerConnection(Player, "New", PlayerTeam.Terrorist, true, Now));
            },
        };
        using var service = new RankPointPresentationService(true, true, players, settings, new PlayerToggleCatalog(), messages);
        await service.CommittedAsync(Change(player!, 0, 5));
        await service.CompleteRoundAsync("round-1");
        Assert.IsEmpty(messages.Requests);
        service.Dispose();
        await service.CommittedAsync(Change(player!, 0, 5));
        Assert.IsEmpty(messages.Requests);
    }

    [TestMethod]
    public async Task PreferenceAndTransportFailuresAreIsolatedAndZeroChangesAreSilent()
    {
        var players = await Players();
        players.TryGet(Player, out var player);
        var messages = new Messages { Fail = true };
        var errors = new List<Exception>();
        using var service = new RankPointPresentationService(true, true, players, new Settings(), new PlayerToggleCatalog(), messages, errors.Add);
        await service.CommittedAsync(Change(player!, 0, 0));
        Assert.IsEmpty(errors);
        await service.CommittedAsync(Change(player!, 0, 5));
        await service.CompleteRoundAsync("round-1");
        Assert.AreEqual(2, errors.Count);
    }

    [TestMethod]
    public void RegistrationCollisionRollsBackPointToggle()
    {
        var toggles = new PlayerToggleCatalog();
        using var reserved = toggles.Register(new AnoCore.Abstractions.Modules.ModuleId("reserved"),
            new PlayerToggleSetting(RankPointPresentationService.SummarySetting, "Reserved", "Reserved"));
        Assert.ThrowsExactly<InvalidOperationException>(() => new RankPointPresentationService(true, true,
            new PlayerRegistry(new AnoEventBus()), new Settings(), toggles, new Messages()));
        Assert.AreEqual(1, toggles.GetAll().Count);
    }

    private static RankPointChange Change(PlayerSnapshot player, long previous, long current)
        => new(Guid.NewGuid(), "combat.death", player, "round-1", previous, current);

    private static async Task<PlayerRegistry> Players()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await players.ConnectAsync(new PlayerConnection(Player, "Player", PlayerTeam.Terrorist, true, Now));
        return players;
    }

    private sealed class Settings : IPlayerSettingsService
    {
        public bool Enabled { get; init; } = true;
        public Func<Task>? BeforeRead { get; init; }
        public async ValueTask<T> GetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, CancellationToken cancellationToken = default)
        {
            if (BeforeRead is not null) await BeforeRead();
            return (T)(object)Enabled;
        }
        public ValueTask SetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, T value, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public ValueTask<bool> ResetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, CancellationToken cancellationToken = default) => throw new AssertFailedException();
    }

    private sealed class Messages : IMessageService
    {
        public bool Fail { get; init; }
        public List<MessageRequest> Requests { get; } = [];
        public ValueTask<MessageDispatchResult> SendAsync(MessageRequest request, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("test message failure");
            Requests.Add(request);
            return ValueTask.FromResult(MessageDispatchResult.DeliveredResult);
        }
    }
}
