using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Targeting;
using AnoCore.Runtime.Permissions;
using AnoCore.Runtime.Targeting;

namespace AnoCore.Tests.Targeting;

[TestClass]
public sealed class TargetAuthorizationServiceTests
{
    private static readonly PlayerId Actor = new(76561198000001001);
    private static readonly PlayerId Target = new(76561198000001002);
    private static readonly PermissionId KickPermission = new("ano.admin.kick");
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 13, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task AuthorizeAsync_RequiresOperationPermissionBeforeTargeting()
    {
        var actor = Player(Actor, "Actor");
        var target = Player(Target, "Target");
        var service = CreateService(
            [actor, target],
            AuthorizationState.Empty);

        var decision = await service.AuthorizeAsync(Actor, target, KickPermission);

        Assert.IsFalse(decision.IsAllowed);
        Assert.AreEqual(TargetAuthorizationFailure.PermissionDenied, decision.Failure);
    }

    [TestMethod]
    public async Task AuthorizeAsync_BlocksSelfUnlessOperationExplicitlyAllowsIt()
    {
        var actor = Player(Actor, "Actor");
        var service = CreateService(
            [actor],
            State(
                Role("admin", 50, Allow("ano.admin.kick")),
                Assignment(Actor, "admin")));

        var denied = await service.AuthorizeAsync(Actor, actor, KickPermission);
        var allowed = await service.AuthorizeAsync(Actor, actor, KickPermission, allowSelf: true);

        Assert.AreEqual(TargetAuthorizationFailure.SelfTargetNotAllowed, denied.Failure);
        Assert.IsTrue(allowed.IsAllowed);
    }

    [TestMethod]
    public async Task AuthorizeAsync_RejectsStaleTargetSessionAfterReconnect()
    {
        var actor = Player(Actor, "Actor");
        var oldTarget = Player(Target, "Target");
        var reconnectedTarget = Player(Target, "Target", sessionId: PlayerSessionId.New());
        var service = CreateService(
            [actor, reconnectedTarget],
            State(
                Role("admin", 100, Allow("ano.admin.kick")),
                Role("player", 0),
                Assignment(Actor, "admin"),
                Assignment(Target, "player")));

        var decision = await service.AuthorizeAsync(Actor, oldTarget, KickPermission);

        Assert.IsFalse(decision.IsAllowed);
        Assert.AreEqual(TargetAuthorizationFailure.StaleTarget, decision.Failure);
    }

    [TestMethod]
    public async Task AuthorizeAsync_RejectsDisconnectedOrMissingActorAndTarget()
    {
        var actor = Player(Actor, "Actor");
        var target = Player(Target, "Target");
        var state = State(
            Role("admin", 100, Allow("ano.admin.kick")),
            Role("player", 0),
            Assignment(Actor, "admin"),
            Assignment(Target, "player"));

        var missingActor = CreateService([target], state);
        var missingTarget = CreateService([actor], state);

        var actorDecision = await missingActor.AuthorizeAsync(Actor, target, KickPermission);
        var targetDecision = await missingTarget.AuthorizeAsync(Actor, target, KickPermission);

        Assert.AreEqual(TargetAuthorizationFailure.ActorNotOnline, actorDecision.Failure);
        Assert.AreEqual(TargetAuthorizationFailure.TargetNotOnline, targetDecision.Failure);
    }

    [TestMethod]
    public async Task AuthorizeAsync_UsesExistingImmunityAndBlocksEqualOrHigherTargets()
    {
        var actor = Player(Actor, "Actor");
        var target = Player(Target, "Target");
        var equal = CreateService(
            [actor, target],
            State(
                Role("actor", 50, Allow("ano.admin.kick")),
                Role("target", 50),
                Assignment(Actor, "actor"),
                Assignment(Target, "target")));
        var stronger = CreateService(
            [actor, target],
            State(
                Role("actor", 100, Allow("ano.admin.kick")),
                Role("target", 50),
                Assignment(Actor, "actor"),
                Assignment(Target, "target")));

        var equalDecision = await equal.AuthorizeAsync(Actor, target, KickPermission);
        var strongerDecision = await stronger.AuthorizeAsync(Actor, target, KickPermission);

        Assert.AreEqual(TargetAuthorizationFailure.TargetImmune, equalDecision.Failure);
        Assert.IsTrue(strongerDecision.IsAllowed);
    }

    private static TargetAuthorizationService CreateService(
        IReadOnlyCollection<PlayerSnapshot> players,
        AuthorizationState state)
    {
        var registry = new StubRegistry(players);
        var authorization = new AuthorizationService(new StubAuthorizationStore(state), state);
        return new TargetAuthorizationService(registry, authorization);
    }

    private static PlayerSnapshot Player(
        PlayerId id,
        string name,
        PlayerSessionId? sessionId = null)
        => new(
            id,
            sessionId ?? PlayerSessionId.New(),
            name,
            true,
            true,
            PlayerTeam.CounterTerrorist,
            Now,
            Now);

    private static AuthorizationState State(
        AuthorizationRole firstRole,
        params object[] members)
    {
        var roles = new List<AuthorizationRole> { firstRole };
        var players = new List<PlayerAuthorization>();
        foreach (var member in members)
        {
            if (member is AuthorizationRole role)
            {
                roles.Add(role);
            }
            else if (member is PlayerAuthorization player)
            {
                players.Add(player);
            }
        }

        return new AuthorizationState(roles, players);
    }

    private static AuthorizationRole Role(string id, int immunity, params PermissionRule[] rules)
        => new(new RoleId(id), immunity, [], rules, []);

    private static PlayerAuthorization Assignment(PlayerId playerId, string role)
        => new(playerId, [new RoleId(role)], []);

    private static PermissionRule Allow(string permission)
        => new(permission, PermissionEffect.Allow);

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

    private sealed class StubAuthorizationStore(AuthorizationState state) : IAuthorizationStore
    {
        public ValueTask<AuthorizationState?> LoadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<AuthorizationState?>(state);

        public ValueTask SaveAsync(AuthorizationState value, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
