using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Targeting;
using AnoCore.Runtime.Targeting;

namespace AnoCore.Tests.Targeting;

[TestClass]
public sealed class PlayerTargetResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 13, 0, 0, TimeSpan.Zero);
    private static readonly PlayerSnapshot Alice = Player(76561198000000001, "Alice", PlayerTeam.CounterTerrorist);
    private static readonly PlayerSnapshot Alicia = Player(76561198000000002, "Alicia", PlayerTeam.Terrorist);
    private static readonly PlayerSnapshot Bob = Player(76561198000000003, "Bob", PlayerTeam.Terrorist);
    private static readonly PlayerSnapshot Spectator = Player(76561198000000004, "Spec", PlayerTeam.Spectator);

    [TestMethod]
    public void Resolve_ExactSteamIdAndExactNameWinDeterministically()
    {
        var resolver = CreateResolver(Alice, Alicia, Bob);

        var bySteamId = resolver.Resolve(Alice.Id.SteamId64.ToString());
        var byName = resolver.Resolve("alice");

        Assert.IsTrue(bySteamId.Accepted);
        Assert.AreEqual(Alice.Id, bySteamId.Targets.Single().Id);
        Assert.IsTrue(byName.Accepted);
        Assert.AreEqual(Alice.Id, byName.Targets.Single().Id);
    }

    [TestMethod]
    public void Resolve_UniquePrefixWorksButAmbiguousPrefixIsRejected()
    {
        var resolver = CreateResolver(Alice, Alicia, Bob);

        var unique = resolver.Resolve("alic i".Replace(" ", string.Empty, StringComparison.Ordinal));
        var ambiguous = resolver.Resolve("ali");

        Assert.IsTrue(unique.Accepted);
        Assert.AreEqual(Alicia.Id, unique.Targets.Single().Id);
        Assert.IsFalse(ambiguous.Accepted);
        Assert.AreEqual(TargetResolutionFailure.Ambiguous, ambiguous.Failure);
        Assert.AreEqual(0, ambiguous.Targets.Count);
    }

    [TestMethod]
    public void Resolve_ExactNameBeatsOtherPrefixMatches()
    {
        var exact = Player(76561198000000005, "Al", PlayerTeam.CounterTerrorist);
        var resolver = CreateResolver(exact, Alice, Alicia);

        var result = resolver.Resolve("al");

        Assert.IsTrue(result.Accepted);
        Assert.AreEqual(exact.Id, result.Targets.Single().Id);
    }

    [TestMethod]
    public void Resolve_SelfRequiresCallerAndExplicitCapability()
    {
        var resolver = CreateResolver(Alice, Bob);

        var forbidden = resolver.Resolve("@me", Alice.Id);
        var missingCaller = resolver.Resolve("@me", null, TargetSelectorCapabilities.Self);
        var allowed = resolver.Resolve("@me", Alice.Id, TargetSelectorCapabilities.Self);

        Assert.AreEqual(TargetResolutionFailure.SelectorNotAllowed, forbidden.Failure);
        Assert.AreEqual(TargetResolutionFailure.CallerRequired, missingCaller.Failure);
        Assert.IsTrue(allowed.Accepted);
        Assert.AreEqual(Alice.Id, allowed.Targets.Single().Id);
    }

    [TestMethod]
    public void Resolve_AllAndTeamsRequireCapabilityAndUseStableSteamIdOrdering()
    {
        var resolver = CreateResolver(Bob, Spectator, Alicia, Alice);
        const TargetSelectorCapabilities capabilities = TargetSelectorCapabilities.All | TargetSelectorCapabilities.Team;

        var all = resolver.Resolve("@all", Alice.Id, capabilities);
        var terrorists = resolver.Resolve("@t", Alice.Id, capabilities);
        var counterTerrorists = resolver.Resolve("@ct", Alice.Id, capabilities);
        var spectators = resolver.Resolve("@spec", Alice.Id, capabilities);

        CollectionAssert.AreEqual(
            new[] { Alice.Id, Alicia.Id, Bob.Id, Spectator.Id },
            all.Targets.Select(player => player.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { Alicia.Id, Bob.Id },
            terrorists.Targets.Select(player => player.Id).ToArray());
        CollectionAssert.AreEqual(new[] { Alice.Id }, counterTerrorists.Targets.Select(player => player.Id).ToArray());
        CollectionAssert.AreEqual(new[] { Spectator.Id }, spectators.Targets.Select(player => player.Id).ToArray());
    }

    [TestMethod]
    public void Resolve_IgnoresDisconnectedSnapshotsAndRejectsUnknownOrUnsupportedSelectors()
    {
        var disconnected = Player(76561198000000006, "Gone", PlayerTeam.Terrorist, isConnected: false);
        var resolver = CreateResolver(Alice, disconnected);

        var disconnectedResult = resolver.Resolve("Gone");
        var unknown = resolver.Resolve("Nobody");
        var unsupported = resolver.Resolve("@all", Alice.Id, TargetSelectorCapabilities.Self);

        Assert.AreEqual(TargetResolutionFailure.NotFound, disconnectedResult.Failure);
        Assert.AreEqual(TargetResolutionFailure.NotFound, unknown.Failure);
        Assert.AreEqual(TargetResolutionFailure.SelectorNotAllowed, unsupported.Failure);
    }

    private static PlayerTargetResolver CreateResolver(params PlayerSnapshot[] players)
        => new(new StubRegistry(players));

    private static PlayerSnapshot Player(
        ulong steamId,
        string name,
        PlayerTeam team,
        bool isConnected = true)
        => new(
            new PlayerId(steamId),
            PlayerSessionId.New(),
            name,
            isConnected,
            true,
            team,
            Now,
            Now);

    private sealed class StubRegistry(IEnumerable<PlayerSnapshot> players) : IPlayerRegistry
    {
        private readonly Dictionary<PlayerId, PlayerSnapshot> _players = players.ToDictionary(player => player.Id);

        public IReadOnlyCollection<PlayerSnapshot> OnlinePlayers => _players.Values.ToArray();

        public bool TryGet(PlayerId id, out PlayerSnapshot? player) => _players.TryGetValue(id, out player);

        public ValueTask<PlayerSnapshot> ConnectAsync(PlayerConnection connection, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<PlayerSnapshot?> UpdateAsync(PlayerStateUpdate update, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<PlayerSnapshot?> DisconnectAsync(
            PlayerId id,
            PlayerSessionId sessionId,
            DateTimeOffset disconnectedAtUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
