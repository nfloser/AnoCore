using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class NativeChatRouterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 28, 2, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task PublicMessage_FormatsForAllConnectedPlayers()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var sender = await ConnectAsync(players, 76561198000012641, PlayerTeam.Terrorist);
        var teammate = await ConnectAsync(players, 76561198000012642, PlayerTeam.Terrorist);
        var opponent = await ConnectAsync(players, 76561198000012643, PlayerTeam.CounterTerrorist);
        var router = Router(players, Format);

        var route = router.Route(sender.Id, "hello", false);

        Assert.IsTrue(route.ShouldIntercept);
        Assert.AreEqual("[R] Player: hello", route.FormattedMessage);
        CollectionAssert.AreEquivalent(
            new[] { sender.Id, teammate.Id, opponent.Id },
            route.Recipients.ToArray());
    }

    [TestMethod]
    public async Task TeamMessage_OnlyTargetsCurrentTeam()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var sender = await ConnectAsync(players, 76561198000012644, PlayerTeam.Terrorist);
        var teammate = await ConnectAsync(players, 76561198000012645, PlayerTeam.Terrorist);
        await ConnectAsync(players, 76561198000012646, PlayerTeam.CounterTerrorist);
        var router = Router(players, Format);

        var route = router.Route(sender.Id, "team", true);

        CollectionAssert.AreEquivalent(
            new[] { sender.Id, teammate.Id },
            route.Recipients.ToArray());
        Assert.AreEqual("(TEAM) [R] Player: team", route.FormattedMessage);
    }

    [TestMethod]
    [DataRow("!anorank")]
    [DataRow(" /anotopranks 2")]
    public async Task Commands_PassThroughWithoutModerationOrFormatting(string message)
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var sender = await ConnectAsync(players, 76561198000012647, PlayerTeam.Terrorist);
        var moderationCalls = 0;
        var formatCalls = 0;
        var router = new NativeChatRouter(
            _ =>
            {
                moderationCalls++;
                return ChatInterceptionDecision.Block;
            },
            players,
            (PlayerId _, PlayerSessionId _, string? _, bool _, out string? formatted) =>
            {
                formatCalls++;
                formatted = null;
                return false;
            });

        var route = router.Route(sender.Id, message, false);

        Assert.IsFalse(route.ShouldIntercept);
        Assert.AreEqual(0, moderationCalls);
        Assert.AreEqual(0, formatCalls);
    }

    [TestMethod]
    public async Task EmptyMessage_IsSuppressedWithoutFormatting()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var sender = await ConnectAsync(players, 76561198000012651, PlayerTeam.Terrorist);
        var formatCalls = 0;
        var router = Router(
            players,
            (PlayerId _, PlayerSessionId _, string? _, bool _, out string? formatted) =>
            {
                formatCalls++;
                formatted = "unexpected";
                return true;
            });

        var route = router.Route(sender.Id, "   ", false);

        Assert.IsTrue(route.ShouldIntercept);
        Assert.IsNull(route.FormattedMessage);
        Assert.AreEqual(0, formatCalls);
    }

    [TestMethod]
    public async Task BlockedSender_IsSuppressedBeforeFormatting()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var sender = await ConnectAsync(players, 76561198000012648, PlayerTeam.Terrorist);
        var formatCalls = 0;
        var router = new NativeChatRouter(
            _ => ChatInterceptionDecision.Block,
            players,
            (PlayerId _, PlayerSessionId _, string? _, bool _, out string? formatted) =>
            {
                formatCalls++;
                formatted = "unexpected";
                return true;
            });

        var route = router.Route(sender.Id, "blocked", false);

        Assert.IsTrue(route.ShouldIntercept);
        Assert.IsNull(route.FormattedMessage);
        Assert.AreEqual(0, formatCalls);
    }

    [TestMethod]
    public async Task MissingOrStaleSnapshot_FailsClosed()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var sender = await ConnectAsync(players, 76561198000012649, PlayerTeam.Terrorist);
        var router = Router(
            players,
            (PlayerId _, PlayerSessionId _, string? _, bool _, out string? formatted) =>
            {
                formatted = null;
                return false;
            });

        var route = router.Route(sender.Id, "not ready", false);

        Assert.IsTrue(route.ShouldIntercept);
        Assert.IsNull(route.FormattedMessage);
        Assert.AreEqual(0, route.Recipients.Count);
    }

    [TestMethod]
    public async Task FormattingDisabled_PreservesAllowedNativeChat()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var sender = await ConnectAsync(players, 76561198000012650, PlayerTeam.Terrorist);
        var router = Router(players, null);

        var route = router.Route(sender.Id, "native", false);

        Assert.IsFalse(route.ShouldIntercept);
    }

    [TestMethod]
    public async Task AcceptedChatCapturesSessionChannelAndUtcWithoutIncludingCommandsOrSuppressedInput()
    {
        var bus = new AnoEventBus();
        var players = new PlayerRegistry(bus);
        var sender = await ConnectAsync(players, 76561198000012652, PlayerTeam.Terrorist);
        var received = new List<PlayerChatAcceptedEvent>();
        using var subscription = bus.Subscribe<PlayerChatAcceptedEvent>((item, _) =>
        {
            received.Add(item);
            return ValueTask.CompletedTask;
        });
        var router = new NativeChatRouter(_ => ChatInterceptionDecision.Allow, players, Format, bus, () => Now);
        router.Route(sender.Id, "team hello", true);
        router.Route(sender.Id, "!anostatus", false);
        router.Route(sender.Id, "   ", false);
        new NativeChatRouter(_ => ChatInterceptionDecision.Block, players, Format, bus).Route(sender.Id, "blocked", false);
        new NativeChatRouter(_ => ChatInterceptionDecision.Allow, players,
            (PlayerId _, PlayerSessionId _, string? _, bool _, out string? formatted) =>
            {
                formatted = null;
                return false;
            }, bus).Route(sender.Id, "not formatted", false);
        Assert.HasCount(1, received);
        Assert.AreEqual(sender, received[0].Sender);
        Assert.AreEqual("team hello", received[0].Message);
        Assert.IsTrue(received[0].IsTeamMessage);
        Assert.AreEqual(Now, received[0].OccurredAtUtc);
    }

    [TestMethod]
    public async Task NativePassThroughPublishesOnlyConnectedSendersAndBoundsPayload()
    {
        var bus = new AnoEventBus();
        var players = new PlayerRegistry(bus);
        var sender = await ConnectAsync(players, 76561198000012653, PlayerTeam.Terrorist);
        var received = new List<PlayerChatAcceptedEvent>();
        using var subscription = bus.Subscribe<PlayerChatAcceptedEvent>((item, _) =>
        {
            received.Add(item);
            return ValueTask.CompletedTask;
        });
        var router = new NativeChatRouter(_ => ChatInterceptionDecision.Allow, players, null, bus);
        Assert.IsFalse(router.Route(sender.Id, new string('a', 2048), false).ShouldIntercept);
        Assert.IsFalse(router.Route(new PlayerId(76561198000012654), "unknown", false).ShouldIntercept);
        Assert.HasCount(1, received);
        Assert.AreEqual(1024, received[0].Message.Length);
    }

    [TestMethod]
    public async Task AsyncObserverFailureDoesNotDelayRoutingOrEscapeDiagnostics()
    {
        var bus = new AnoEventBus();
        var players = new PlayerRegistry(bus);
        var sender = await ConnectAsync(players, 76561198000012655, PlayerTeam.Terrorist);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = bus.Subscribe<PlayerChatAcceptedEvent>(async (_, _) =>
        {
            await release.Task;
            throw new InvalidOperationException("observer");
        });
        var router = new NativeChatRouter(_ => ChatInterceptionDecision.Allow, players, Format, bus,
            reportError: _ =>
            {
                reported.SetResult();
                throw new InvalidOperationException("diagnostics");
            });
        var route = router.Route(sender.Id, "hello", false);
        Assert.AreEqual("[R] Player: hello", route.FormattedMessage);
        release.SetResult();
        await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static NativeChatRouter Router(
        IPlayerRegistry players,
        ChatSnapshotFormatter? formatter)
        => new(_ => ChatInterceptionDecision.Allow, players, formatter);

    private static bool Format(
        PlayerId playerId,
        PlayerSessionId sessionId,
        string? message,
        bool isTeamMessage,
        out string? formatted)
    {
        formatted = isTeamMessage
            ? $"(TEAM) [R] Player: {message}"
            : $"[R] Player: {message}";
        return true;
    }

    private static ValueTask<PlayerSnapshot> ConnectAsync(
        IPlayerRegistry players,
        ulong steamId,
        PlayerTeam team)
        => players.ConnectAsync(new PlayerConnection(
            new PlayerId(steamId),
            "Player",
            team,
            isAlive: true,
            Now));
}
