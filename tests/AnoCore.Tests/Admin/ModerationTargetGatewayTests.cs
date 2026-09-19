using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Targeting;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Targeting;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ModerationTargetGatewayTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 13, 45, 0, TimeSpan.Zero);
    private static readonly PlayerId Actor = new(76561198000003001);
    private static readonly PlayerId Target = new(76561198000003002);
    private static readonly PermissionId BanPermission = new("ano.admin.ban");

    [TestMethod]
    public async Task ResolveAsync_OnlineNameUsesCentralTargetAuthorization()
    {
        var actor = Player(Actor, "Admin");
        var target = Player(Target, "Target");
        var authorization = new StubAuthorization
        {
            PermissionAllowed = true,
            TargetAllowed = false,
        };
        var gateway = CreateGateway([actor, target], authorization);

        var denied = await gateway.ResolveAsync("target", Actor, BanPermission);

        Assert.IsFalse(denied.Accepted);
        Assert.AreEqual(ModerationTargetFailure.TargetImmune, denied.Failure);
        Assert.AreEqual(1, authorization.PermissionChecks);
        Assert.AreEqual(1, authorization.TargetChecks);

        authorization.TargetAllowed = true;
        var allowed = await gateway.ResolveAsync("target", Actor, BanPermission);

        Assert.IsTrue(allowed.Accepted);
        Assert.AreEqual(Target, allowed.Target!.Id);
        Assert.IsTrue(allowed.Target.IsOnline);
        Assert.AreEqual(target.SessionId, allowed.Target.OnlinePlayer!.SessionId);
    }

    [TestMethod]
    public async Task ResolveAsync_AmbiguousOnlinePrefixIsRejectedWithoutGuessing()
    {
        var actor = Player(Actor, "Admin");
        var first = Player(Target, "Alice");
        var second = Player(new PlayerId(76561198000003003), "Alicia");
        var authorization = new StubAuthorization
        {
            PermissionAllowed = true,
            TargetAllowed = true,
        };
        var gateway = CreateGateway([actor, first, second], authorization);

        var result = await gateway.ResolveAsync("ali", Actor, BanPermission);

        Assert.IsFalse(result.Accepted);
        Assert.AreEqual(ModerationTargetFailure.Ambiguous, result.Failure);
        Assert.AreEqual(0, authorization.PermissionChecks);
        Assert.AreEqual(0, authorization.TargetChecks);
    }

    [TestMethod]
    public async Task ResolveAsync_OfflineSteamIdRequiresPermissionAndImmunity()
    {
        var actor = Player(Actor, "Admin");
        var authorization = new StubAuthorization
        {
            PermissionAllowed = false,
            TargetAllowed = false,
        };
        var gateway = CreateGateway([actor], authorization);
        var selector = Target.SteamId64.ToString();

        var permissionDenied = await gateway.ResolveAsync(selector, Actor, BanPermission);
        Assert.AreEqual(ModerationTargetFailure.PermissionDenied, permissionDenied.Failure);

        authorization.PermissionAllowed = true;
        var immune = await gateway.ResolveAsync(selector, Actor, BanPermission);
        Assert.AreEqual(ModerationTargetFailure.TargetImmune, immune.Failure);

        authorization.TargetAllowed = true;
        var allowed = await gateway.ResolveAsync(selector, Actor, BanPermission);

        Assert.IsTrue(allowed.Accepted);
        Assert.AreEqual(Target, allowed.Target!.Id);
        Assert.IsFalse(allowed.Target.IsOnline);
        Assert.IsNull(allowed.Target.OnlinePlayer);
    }

    [TestMethod]
    public async Task ResolveAsync_OfflineSteamIdRequiresOnlineActor()
    {
        var authorization = new StubAuthorization
        {
            PermissionAllowed = true,
            TargetAllowed = true,
        };
        var gateway = CreateGateway([], authorization);

        var result = await gateway.ResolveAsync(Target.SteamId64.ToString(), Actor, BanPermission);

        Assert.IsFalse(result.Accepted);
        Assert.AreEqual(ModerationTargetFailure.ActorNotOnline, result.Failure);
        Assert.AreEqual(0, authorization.PermissionChecks);
        Assert.AreEqual(0, authorization.TargetChecks);
    }

    [TestMethod]
    public async Task ResolveAsync_ConsoleCanTargetOnlineOrOfflineWithoutPlayerPermissionChecks()
    {
        var target = Player(Target, "Target");
        var authorization = new StubAuthorization
        {
            PermissionAllowed = false,
            TargetAllowed = false,
        };
        var gateway = CreateGateway([target], authorization);

        var online = await gateway.ResolveAsync("Target", null, BanPermission);
        var offline = await gateway.ResolveAsync("76561198000003999", null, BanPermission);

        Assert.IsTrue(online.Accepted);
        Assert.IsTrue(online.Target!.IsOnline);
        Assert.IsTrue(offline.Accepted);
        Assert.IsFalse(offline.Target!.IsOnline);
        Assert.AreEqual(0, authorization.PermissionChecks);
        Assert.AreEqual(0, authorization.TargetChecks);
    }

    [TestMethod]
    public async Task ResolveAsync_RejectsSelfSelectorsMultiTargetSelectorsAndUnknownNames()
    {
        var actor = Player(Actor, "Admin");
        var authorization = new StubAuthorization
        {
            PermissionAllowed = true,
            TargetAllowed = true,
        };
        var gateway = CreateGateway([actor], authorization);

        var self = await gateway.ResolveAsync(Actor.SteamId64.ToString(), Actor, BanPermission);
        var all = await gateway.ResolveAsync("@all", Actor, BanPermission);
        var unknown = await gateway.ResolveAsync("Nobody", Actor, BanPermission);

        Assert.AreEqual(ModerationTargetFailure.SelfTargetNotAllowed, self.Failure);
        Assert.AreEqual(ModerationTargetFailure.SelectorNotAllowed, all.Failure);
        Assert.AreEqual(ModerationTargetFailure.NotFound, unknown.Failure);
    }

    private static ModerationTargetGateway CreateGateway(
        IReadOnlyCollection<PlayerSnapshot> players,
        StubAuthorization authorization)
    {
        var registry = new StubRegistry(players);
        return new ModerationTargetGateway(
            registry,
            new PlayerTargetResolver(registry),
            new TargetAuthorizationService(registry, authorization),
            authorization);
    }

    private static PlayerSnapshot Player(PlayerId id, string name)
        => new(
            id,
            PlayerSessionId.New(),
            name,
            true,
            true,
            PlayerTeam.CounterTerrorist,
            Now,
            Now);

    private sealed class StubRegistry(IEnumerable<PlayerSnapshot> players) : IPlayerRegistry
    {
        private readonly Dictionary<PlayerId, PlayerSnapshot> _players =
            players.ToDictionary(player => player.Id);

        public IReadOnlyCollection<PlayerSnapshot> OnlinePlayers => _players.Values.ToArray();

        public bool TryGet(PlayerId id, out PlayerSnapshot? player)
            => _players.TryGetValue(id, out player);

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

    private sealed class StubAuthorization : IAuthorizationService
    {
        public bool PermissionAllowed { get; set; }

        public bool TargetAllowed { get; set; }

        public int PermissionChecks { get; private set; }

        public int TargetChecks { get; private set; }

        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PermissionChecks++;
            return ValueTask.FromResult(PermissionAllowed);
        }

        public ValueTask<AuthorizationDecision> EvaluateAsync(
            PlayerId playerId,
            PermissionId permission,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PermissionChecks++;
            return ValueTask.FromResult(
                PermissionAllowed
                    ? new AuthorizationDecision(
                        true,
                        PermissionEffect.Allow,
                        AuthorizationDecisionSource.Direct,
                        new PermissionRule(permission.Value, PermissionEffect.Allow),
                        null)
                    : AuthorizationDecision.DeniedByDefault);
        }

        public ValueTask<int> GetImmunityAsync(
            PlayerId playerId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }

        public ValueTask<bool> CanTargetAsync(
            PlayerId actor,
            PlayerId target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TargetChecks++;
            return ValueTask.FromResult(TargetAllowed);
        }

        public ValueTask<bool> HasTagAsync(
            PlayerId playerId,
            string tag,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }

        public ValueTask ReloadAsync(CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
