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

    [DataTestMethod]
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
            isAlive: true,
            team,
            Now));
}
