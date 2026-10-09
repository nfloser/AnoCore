using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ServerInfoTests
{
    [TestMethod]
    public async Task WelcomeIsPrivateSessionBoundAndInfoRotatesWithoutCatchupBurst()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonConfigStore(root);
            await store.SaveAsync("server-info", new ServerInfoConfiguration
            {
                Enabled = true, WelcomeDelaySeconds = 3, IntervalSeconds = 30,
                WelcomeMessages = ["{Green}Hi {Red}{player.name} {map.name} {playtime.total}"],
                InfoMessages = [["{Yellow}one"], ["{Purple}two"]],
            });
            var players = new PlayerRegistry(new AnoEventBus());
            var now = DateTimeOffset.UtcNow;
            var player = await players.ConnectAsync(new PlayerConnection(new PlayerId(76561198000012641),
                "Nille\x07{Red}", PlayerTeam.Terrorist, true, now));
            var messages = new Messages();
            using var service = await ServerInfoService.CreateAsync(store, players, messages, new Playtime());
            await service.TickAsync(now, "map{Red}\x07");
            Assert.AreEqual(0, messages.Sent.Count);
            await service.TickAsync(now.AddSeconds(3), "map{Red}\x07");
            Assert.AreEqual(1, messages.Sent.Count);
            var welcome = messages.Sent[0];
            Assert.AreEqual(player.Id, welcome.Target.PlayerId);
            Assert.AreEqual(player.SessionId, welcome.Target.SessionId);
            StringAssert.StartsWith(welcome.Text, "\x04Hi \x07Nille");
            Assert.AreEqual(1, welcome.Text.Count(c => c == '\x07'));
            StringAssert.Contains(welcome.Text, "2d 3h 4m");
            await service.TickAsync(now.AddSeconds(4), "map");
            Assert.AreEqual(1, messages.Sent.Count);
            await service.TickAsync(now.AddSeconds(30), "map");
            StringAssert.StartsWith(messages.Sent[^1].Text, "\x09one");
            await service.TickAsync(now.AddHours(1), "map");
            Assert.AreEqual(3, messages.Sent.Count);
            StringAssert.StartsWith(messages.Sent[^1].Text, "\x0Etwo");
            service.Dispose();
            await service.TickAsync(now.AddHours(2), "map");
            Assert.AreEqual(3, messages.Sent.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ReloadEnablesMessagesAndStaleDatabaseReadCannotWelcomeReplacementSession()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonConfigStore(root);
            var reloads = new ConfigReloadRegistry();
            var players = new PlayerRegistry(new AnoEventBus());
            var now = DateTimeOffset.UtcNow;
            var connection = new PlayerConnection(new PlayerId(76561198000012641), "Nille", PlayerTeam.Terrorist, true, now);
            var old = await players.ConnectAsync(connection);
            var messages = new Messages();
            var read = new TaskCompletionSource<PlaytimeTotals>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var repository = new Playtime { Read = () => { started.TrySetResult(); return new(read.Task); } };
            using var service = await ServerInfoService.CreateAsync(store, players, messages, repository, reloads);
            await service.TickAsync(now.AddSeconds(10), "map");
            Assert.AreEqual(0, messages.Sent.Count); // disabled by default
            await store.SaveAsync("server-info", new ServerInfoConfiguration
            { Enabled = true, WelcomeDelaySeconds = 0, WelcomeMessages = ["{playtime.total}"], InfoMessages = [] });
            await reloads.ReloadAsync("server-info");
            var tick = service.TickAsync(now.AddSeconds(11), "map").AsTask();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await players.DisconnectAsync(old.Id, old.SessionId, now.AddSeconds(11));
            var replacement = await players.ConnectAsync(new PlayerConnection(old.Id, "Replacement",
                PlayerTeam.Terrorist, true, now.AddSeconds(12)));
            read.SetResult(new(TimeSpan.FromHours(4), TimeSpan.Zero));
            await tick;
            Assert.AreEqual(0, messages.Sent.Count);
            await service.TickAsync(now.AddSeconds(13), "map");
            Assert.AreEqual(1, messages.Sent.Count);
            Assert.AreEqual(replacement.SessionId, messages.Sent[0].Target.SessionId);
            await store.SaveAsync("server-info", new ServerInfoConfiguration { IntervalSeconds = 1 });
            await Assert.ThrowsExactlyAsync<ConfigValidationException>(
                () => reloads.ReloadAsync("server-info").AsTask());
            service.Dispose();
            Assert.IsFalse(reloads.Configurations.Any(c => c.Name == "server-info"));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void ConfigurationRejectsUnknownTokensUnboundedIntervalsAndControls()
    {
        Assert.AreEqual(0, ServerInfoConfiguration.Validate(new()).Count);
        Assert.IsTrue(ServerInfoConfiguration.Validate(new() { IntervalSeconds = 1 }).Count > 0);
        Assert.IsTrue(ServerInfoConfiguration.Validate(new() { WelcomeMessages = ["{Bogus}"] }).Count > 0);
        Assert.IsTrue(ServerInfoConfiguration.Validate(new() { WelcomeMessages = ["hello\nworld"] }).Count > 0);
        Assert.IsTrue(ServerInfoConfiguration.Validate(new() { WelcomeMessages = [string.Concat(Enumerable.Repeat("{map.name}", 12))] }).Count > 0);
    }

    private sealed class Messages : IMessageService
    {
        public List<MessageRequest> Sent { get; } = [];
        public ValueTask<MessageDispatchResult> SendAsync(MessageRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent.Add(request);
            return ValueTask.FromResult(MessageDispatchResult.DeliveredResult);
        }
    }

    private sealed class Playtime : IPlaytimeRepository
    {
        public Func<ValueTask<PlaytimeTotals>>? Read { get; init; }
        public ValueTask OpenAsync(PlayerId id, PlayerSessionId session, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask AdvanceAsync(PlayerId id, PlayerSessionId session, DateTimeOffset at, bool close = false, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<PlaytimeTotals> ReadAsync(PlayerId id, DateOnly day, CancellationToken cancellationToken = default)
            => Read?.Invoke() ?? ValueTask.FromResult(new PlaytimeTotals(new TimeSpan(2, 3, 4, 0), TimeSpan.Zero));
        public ValueTask<IReadOnlyList<PlaytimeRankEntry>> GetTopAsync(int limit, int offset, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<PlaytimeRankEntry>>([]);
    }
}
