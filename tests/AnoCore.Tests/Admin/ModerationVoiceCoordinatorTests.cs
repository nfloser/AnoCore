using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ModerationVoiceCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 15, 30, 0, TimeSpan.Zero);
    private static readonly PlayerId SenderId = new(76561198000008101);
    private static readonly PlayerId ListenerAId = new(76561198000008102);
    private static readonly PlayerId ListenerBId = new(76561198000008103);

    [TestMethod]
    public void Reconcile_BlockedSenderIsMutedForEveryOtherOnlineListener()
    {
        var sender = Player(SenderId, "Sender");
        var listenerA = Player(ListenerAId, "A");
        var listenerB = Player(ListenerBId, "B");
        var players = new StubPlayers(sender, listenerA, listenerB);
        var transport = new StubTransport();
        var coordinator = new ModerationVoiceCoordinator(
            players,
            new ModerationVoiceGate(new StubPolicy(SenderId)),
            transport);

        coordinator.Reconcile();

        Assert.AreEqual(
            ModerationVoiceOverride.Mute,
            transport.Get(listenerA, sender));
        Assert.AreEqual(
            ModerationVoiceOverride.Mute,
            transport.Get(listenerB, sender));
        Assert.IsFalse(transport.HasPair(sender, sender));
    }

    [TestMethod]
    public void Reconcile_UnmuteRestoresOriginalOverridesInsteadOfForcingDefault()
    {
        var sender = Player(SenderId, "Sender");
        var listenerA = Player(ListenerAId, "A");
        var listenerB = Player(ListenerBId, "B");
        var players = new StubPlayers(sender, listenerA, listenerB);
        var policy = new StubPolicy(SenderId);
        var gate = new ModerationVoiceGate(policy);
        var transport = new StubTransport();
        transport.SetInitial(listenerA, sender, ModerationVoiceOverride.Default);
        transport.SetInitial(listenerB, sender, ModerationVoiceOverride.Hear);
        var coordinator = new ModerationVoiceCoordinator(players, gate, transport);

        coordinator.Reconcile();
        policy.Blocked.Clear();
        coordinator.Reconcile();

        Assert.AreEqual(
            ModerationVoiceOverride.Default,
            transport.Get(listenerA, sender));
        Assert.AreEqual(
            ModerationVoiceOverride.Hear,
            transport.Get(listenerB, sender));
    }

    [TestMethod]
    public void Reconcile_RepeatedMuteDoesNotReplaceRememberedOriginalWithOwnedMute()
    {
        var sender = Player(SenderId, "Sender");
        var listener = Player(ListenerAId, "Listener");
        var players = new StubPlayers(sender, listener);
        var policy = new StubPolicy(SenderId);
        var gate = new ModerationVoiceGate(policy);
        var transport = new StubTransport();
        transport.SetInitial(listener, sender, ModerationVoiceOverride.Hear);
        var coordinator = new ModerationVoiceCoordinator(players, gate, transport);

        coordinator.Reconcile();
        coordinator.Reconcile();
        policy.Blocked.Clear();
        coordinator.Reconcile();

        Assert.AreEqual(
            ModerationVoiceOverride.Hear,
            transport.Get(listener, sender));
    }

    [TestMethod]
    public void Reconcile_NewListenerIsMutedWhileSenderRemainsBlocked()
    {
        var sender = Player(SenderId, "Sender");
        var listenerA = Player(ListenerAId, "A");
        var listenerB = Player(ListenerBId, "B");
        var players = new StubPlayers(sender, listenerA);
        var transport = new StubTransport();
        var coordinator = new ModerationVoiceCoordinator(
            players,
            new ModerationVoiceGate(new StubPolicy(SenderId)),
            transport);

        coordinator.Reconcile();
        players.Set(sender, listenerA, listenerB);
        coordinator.Reconcile();

        Assert.AreEqual(
            ModerationVoiceOverride.Mute,
            transport.Get(listenerB, sender));
    }

    [TestMethod]
    public void Reconcile_ReconnectDoesNotRestorePreviousSessionOverrideOntoNewSession()
    {
        var oldSender = Player(SenderId, "Sender", PlayerSessionId.New());
        var listener = Player(ListenerAId, "Listener");
        var players = new StubPlayers(oldSender, listener);
        var gate = new StubGate(SenderId);
        var transport = new StubTransport();
        transport.SetInitial(listener, oldSender, ModerationVoiceOverride.Hear);
        var coordinator = new ModerationVoiceCoordinator(players, gate, transport);

        coordinator.Reconcile();

        var newSender = Player(SenderId, "Sender", PlayerSessionId.New());
        transport.SetInitial(listener, newSender, ModerationVoiceOverride.Default);
        players.Set(newSender, listener);
        policy.Blocked.Clear();
        coordinator.Reconcile();

        Assert.AreEqual(
            ModerationVoiceOverride.Default,
            transport.Get(listener, newSender));
        Assert.AreEqual(
            ModerationVoiceOverride.Mute,
            transport.Get(listener, oldSender));
    }

    [TestMethod]
    public void Dispose_RestoresOwnedOverridesForStillCurrentSessions()
    {
        var sender = Player(SenderId, "Sender");
        var listener = Player(ListenerAId, "Listener");
        var players = new StubPlayers(sender, listener);
        var transport = new StubTransport();
        transport.SetInitial(listener, sender, ModerationVoiceOverride.Hear);
        var coordinator = new ModerationVoiceCoordinator(
            players,
            new ModerationVoiceGate(new StubPolicy(SenderId)),
            transport);

        coordinator.Reconcile();
        coordinator.Dispose();

        Assert.AreEqual(
            ModerationVoiceOverride.Hear,
            transport.Get(listener, sender));

        coordinator.Dispose();
    }

    private static PlayerSnapshot Player(
        PlayerId id,
        string name,
        PlayerSessionId? session = null)
        => new(
            id,
            session ?? PlayerSessionId.New(),
            name,
            true,
            true,
            PlayerTeam.CounterTerrorist,
            Now,
            Now);

    private sealed class StubPolicy(params PlayerId[] blocked)
        : IModerationCommunicationPolicy
    {
        public HashSet<PlayerId> Blocked { get; } = [.. blocked];

        public CommunicationRestrictionDecision Evaluate(
            PlayerId playerId,
            CommunicationChannel channel)
        {
            Assert.AreEqual(CommunicationChannel.Voice, channel);
            return Blocked.Contains(playerId)
                ? CommunicationRestrictionDecision.Blocked
                : CommunicationRestrictionDecision.Allowed;
        }
    }

    private sealed class StubPlayers(params PlayerSnapshot[] players) : IPlayerRegistry
    {
        private PlayerSnapshot[] _players = players;

        public IReadOnlyCollection<PlayerSnapshot> OnlinePlayers => _players;

        public void Set(params PlayerSnapshot[] players) => _players = players;

        public bool TryGet(PlayerId id, out PlayerSnapshot? player)
        {
            player = _players.FirstOrDefault(value => value.Id == id);
            return player is not null;
        }

        public ValueTask<PlayerSnapshot> ConnectAsync(
            PlayerConnection connection,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<PlayerSnapshot?> UpdateAsync(
            PlayerStateUpdate update,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<PlayerSnapshot?> DisconnectAsync(
            PlayerId id,
            PlayerSessionId sessionId,
            DateTimeOffset disconnectedAtUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubTransport : IModerationVoiceTransport
    {
        private readonly Dictionary<Pair, ModerationVoiceOverride> _values = [];

        public void SetInitial(
            PlayerSnapshot listener,
            PlayerSnapshot sender,
            ModerationVoiceOverride value)
            => _values[Pair.Of(listener, sender)] = value;

        public ModerationVoiceOverride Get(
            PlayerSnapshot listener,
            PlayerSnapshot sender)
            => _values.GetValueOrDefault(
                Pair.Of(listener, sender),
                ModerationVoiceOverride.Default);

        public bool HasPair(
            PlayerSnapshot listener,
            PlayerSnapshot sender)
            => _values.ContainsKey(Pair.Of(listener, sender));

        public bool TryGetOverride(
            PlayerSnapshot listener,
            PlayerSnapshot sender,
            out ModerationVoiceOverride value)
        {
            value = Get(listener, sender);
            return true;
        }

        public bool TrySetOverride(
            PlayerSnapshot listener,
            PlayerSnapshot sender,
            ModerationVoiceOverride value)
        {
            _values[Pair.Of(listener, sender)] = value;
            return true;
        }

        private sealed record Pair(
            PlayerId ListenerId,
            PlayerSessionId ListenerSession,
            PlayerId SenderId,
            PlayerSessionId SenderSession)
        {
            public static Pair Of(
                PlayerSnapshot listener,
                PlayerSnapshot sender)
                => new(
                    listener.Id,
                    listener.SessionId,
                    sender.Id,
                    sender.SessionId);
        }
    }
}
