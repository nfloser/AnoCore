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
        Assert.AreEqual("[ALL] [R] Player: hello", route.FormattedMessage);
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
        Assert.AreEqual("[TEAM] [R] Player: team", route.FormattedMessage);
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
        Assert.AreEqual("[ALL] [R] Player: hello", route.FormattedMessage);
        release.SetResult();
        await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task RolePrefixReadsCurrentMembershipAndPreservesTeamRoutingAndSafeFormatting()
    {
        var root = Path.Combine(Path.GetTempPath(), "anocore-role-chat-route", Guid.NewGuid().ToString("N"));
        try
        {
            var bus = new AnoEventBus();
            var players = new PlayerRegistry(bus);
            var sender = await players.ConnectAsync(new PlayerConnection(new PlayerId(76561198000012656),
                "Nils\x07 {rank.tag}", PlayerTeam.Terrorist, true, Now));
            var teammate = await ConnectAsync(players, 76561198000012657, PlayerTeam.Terrorist);
            await ConnectAsync(players, 76561198000012658, PlayerTeam.CounterTerrorist);
            using var formatter = await ChatMessageFormatter.CreateAsync(new AnoCore.Runtime.Configuration.JsonConfigStore(root),
                new AnoCore.Runtime.Placeholders.PlaceholderRegistry());
            using var snapshots = new ChatFormatSnapshotLifecycle(bus, formatter);
            await snapshots.WarmExistingAsync(players.OnlinePlayers);
            var policy = RoleChatTagPolicy.Compile(new RoleChatTagConfiguration
            {
                Enabled = true,
                Groups = [new("#css/host", 1, new("[HOST]", "Purple"))],
            });
            var groups = new HashSet<string> { "#css/host" };
            var router = new NativeChatRouter(_ => ChatInterceptionDecision.Allow, players, snapshots.TryFormat)
            {
                RolePrefix = player => policy.Resolve(player.Id, groups, player.Team),
            };
            var first = router.Route(sender.Id, "hello\x04 {chat.tag}", true);
            StringAssert.StartsWith(first.FormattedMessage!, "[TEAM] \x0E[HOST]\x01 ");
            StringAssert.Contains(first.FormattedMessage!, "Nils  {rank.tag}: hello  {chat.tag}");
            var nativeText = NativeChatText.Prepare(first.FormattedMessage!);
            StringAssert.StartsWith(nativeText, " [TEAM] \x0E[HOST]\x01 ");
            Assert.IsFalse(nativeText.Contains('\x07'));
            Assert.IsFalse(nativeText.Contains('\x04'));
            CollectionAssert.AreEquivalent(new[] { sender.Id, teammate.Id }, first.Recipients.ToArray());
            groups.Clear(); // no reconnect/snapshot refresh needed when CSS removes a role
            StringAssert.StartsWith(router.Route(sender.Id, "next", false).FormattedMessage!, "[ALL] \x04[ANOMEME]\x01 ");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task RolePrefixIsNotReadForCommandsModeratedOrStaleMessagesAndFailureIsSuppressed()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var sender = await ConnectAsync(players, 76561198000012659, PlayerTeam.Terrorist);
        var calls = 0;
        Func<PlayerSnapshot, string?> prefix = _ => { calls++; throw new InvalidOperationException("lookup failed"); };
        var router = new NativeChatRouter(_ => ChatInterceptionDecision.Allow, players, Format)
        {
            RolePrefix = prefix,
        };
        Assert.IsFalse(router.Route(sender.Id, "!anostatus", false).ShouldIntercept);
        Assert.IsNull(new NativeChatRouter(_ => ChatInterceptionDecision.Block, players, Format)
        { RolePrefix = prefix }.Route(sender.Id, "blocked", false).FormattedMessage);
        Assert.IsNull(router.Route(new PlayerId(76561198000012660), "missing", false).FormattedMessage);
        Assert.AreEqual(0, calls);
        var failure = router.Route(sender.Id, "allowed", false);
        Assert.IsTrue(failure.ShouldIntercept);
        Assert.IsNull(failure.FormattedMessage);
        Assert.IsEmpty(failure.Recipients);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow(".map 123456789", false)]
    [DataRow("  .MAP de_mirage", true)]
    [DataRow(".ready", false)]
    [DataRow(".pause", true)]
    public async Task ExternalDotCommandsPassThroughBeforeModerationSnapshotsPrefixesAndEvents(string message, bool team)
    {
        var bus = new AnoEventBus();
        var players = new PlayerRegistry(bus);
        var sender = await ConnectAsync(players, 76561198000012661, PlayerTeam.Terrorist);
        var observed = 0;
        using var subscription = bus.Subscribe<PlayerChatAcceptedEvent>((_, _) =>
        { observed++; return ValueTask.CompletedTask; });
        var configuration = ChatFormatConfiguration.Default;
        var router = new NativeChatRouter(_ => throw new AssertFailedException("moderation"), players,
            (PlayerId _, PlayerSessionId _, string? _, bool _, out string? formatted) =>
            { formatted = null; throw new AssertFailedException("formatter"); }, bus)
        {
            IsExternalCommand = configuration.IsPassthroughCommand,
            RolePrefix = _ => throw new AssertFailedException("prefix"),
        };
        Assert.IsFalse(router.Route(sender.Id, message, team).ShouldIntercept);
        Assert.AreEqual(0, observed);
    }

    [TestMethod]
    [DataRow(".mapx 123456789")]
    [DataRow(".map;quit")]
    [DataRow(".unknown hello")]
    [DataRow("...")]
    public async Task UnlistedOrNonExactDotTextStillPassesThroughModeration(string message)
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var sender = await ConnectAsync(players, 76561198000012662, PlayerTeam.Terrorist);
        var calls = 0;
        var router = new NativeChatRouter(_ => { calls++; return ChatInterceptionDecision.Block; }, players, Format)
        { IsExternalCommand = ChatFormatConfiguration.Default.IsPassthroughCommand };
        Assert.IsTrue(router.Route(sender.Id, message, false).ShouldIntercept);
        Assert.AreEqual(1, calls);
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
