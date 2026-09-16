using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Permissions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Permissions;

[TestClass]
public sealed class AuthorizationServiceTests
{
    private static readonly PlayerId Alice = new(76561198000000101);
    private static readonly PlayerId Bob = new(76561198000000102);

    [TestMethod]
    public void PermissionRule_NormalizesAndMatchesHierarchicalWildcards()
    {
        var wildcard = new PermissionRule("ANO.ADMIN.*", PermissionEffect.Allow);
        var exact = new PermissionRule("ano.admin.kick", PermissionEffect.Allow);

        Assert.AreEqual("ano.admin.*", wildcard.Pattern);
        Assert.IsTrue(wildcard.Matches(new PermissionId("ano.admin.kick")));
        Assert.IsTrue(wildcard.Matches(new PermissionId("ano.admin.ban.permanent")));
        Assert.IsFalse(wildcard.Matches(new PermissionId("ano.stats.view")));
        Assert.IsTrue(exact.Matches(new PermissionId("ano.admin.kick")));
        Assert.IsFalse(exact.Matches(new PermissionId("ano.admin.kick.force")));
    }

    [TestMethod]
    public void PermissionRule_RejectsInvalidWildcardPlacement()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PermissionRule("ano.*.kick", PermissionEffect.Allow));
        Assert.ThrowsExactly<ArgumentException>(() => new PermissionRule("ano.admin.k*", PermissionEffect.Allow));
    }

    [TestMethod]
    public async Task EvaluateAsync_InheritsPermissionsFromParentRole()
    {
        var service = CreateService(new AuthorizationState(
            [
                Role("helper", rules: [Allow("ano.admin.kick")]),
                Role("moderator", parents: [new RoleId("helper")]),
            ],
            [Assignment(Alice, "moderator")]));

        var decision = await service.EvaluateAsync(Alice, new PermissionId("ano.admin.kick"));

        Assert.IsTrue(decision.IsAllowed);
        Assert.AreEqual(AuthorizationDecisionSource.Role, decision.Source);
        Assert.AreEqual("helper", decision.Role?.Value);
    }

    [TestMethod]
    public async Task EvaluateAsync_UsesMostSpecificRuleThenDenyOnTie()
    {
        var service = CreateService(new AuthorizationState(
            [Role("admin", rules: [Allow("ano.admin.*"), Deny("ano.admin.ban"), Allow("ano.admin.ban")])],
            [Assignment(Alice, "admin")]));

        var kick = await service.EvaluateAsync(Alice, new PermissionId("ano.admin.kick"));
        var ban = await service.EvaluateAsync(Alice, new PermissionId("ano.admin.ban"));

        Assert.IsTrue(kick.IsAllowed);
        Assert.IsFalse(ban.IsAllowed);
        Assert.AreEqual(PermissionEffect.Deny, ban.Effect);
    }

    [TestMethod]
    public async Task EvaluateAsync_DirectRulesOverrideRoleRules()
    {
        var service = CreateService(new AuthorizationState(
            [Role("restricted", rules: [Deny("ano.admin.kick"), Allow("ano.stats.*")])],
            [new PlayerAuthorization(Alice, [new RoleId("restricted")], [Allow("ano.admin.kick"), Deny("ano.stats.view")])]));

        Assert.IsTrue((await service.EvaluateAsync(Alice, new PermissionId("ano.admin.kick"))).IsAllowed);
        Assert.IsFalse((await service.EvaluateAsync(Alice, new PermissionId("ano.stats.view"))).IsAllowed);
    }

    [TestMethod]
    public void AuthorizationState_RejectsCyclicRoleInheritance()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new AuthorizationState(
            [
                Role("a", parents: [new RoleId("b")]),
                Role("b", parents: [new RoleId("c")]),
                Role("c", parents: [new RoleId("a")]),
            ],
            []));
    }

    [TestMethod]
    public async Task Immunity_UsesHighestInheritedRoleAndBlocksEqualOrLowerActors()
    {
        var service = CreateService(new AuthorizationState(
            [
                Role("helper", immunity: 10),
                Role("admin", immunity: 50, parents: [new RoleId("helper")]),
                Role("owner", immunity: 100),
            ],
            [
                Assignment(Alice, "admin"),
                Assignment(Bob, "owner"),
            ]));

        Assert.AreEqual(50, await service.GetImmunityAsync(Alice));
        Assert.AreEqual(100, await service.GetImmunityAsync(Bob));
        Assert.IsFalse(await service.CanTargetAsync(Alice, Bob));
        Assert.IsTrue(await service.CanTargetAsync(Bob, Alice));
        Assert.IsTrue(await service.CanTargetAsync(Alice, Alice));
    }

    [TestMethod]
    public async Task HasTagAsync_InheritsGenericRoleTags()
    {
        var service = CreateService(new AuthorizationState(
            [
                Role("supporter", tags: ["vip", "supporter"]),
                Role("moderator", parents: [new RoleId("supporter")]),
            ],
            [Assignment(Alice, "moderator")]));

        Assert.IsTrue(await service.HasTagAsync(Alice, "VIP"));
        Assert.IsFalse(await service.HasTagAsync(Alice, "tournament-admin"));
    }

    [TestMethod]
    public async Task ReloadAsync_ReplacesCachedPolicyAtomically()
    {
        var store = new InMemoryAuthorizationStore(new AuthorizationState(
            [Role("viewer", rules: [Allow("ano.stats.view")])],
            [Assignment(Alice, "viewer")]));
        var service = new AuthorizationService(store);
        await service.ReloadAsync();
        Assert.IsTrue(await service.HasPermissionAsync(Alice, new PermissionId("ano.stats.view")));

        store.State = new AuthorizationState(
            [Role("viewer", rules: [Deny("ano.stats.view")])],
            [Assignment(Alice, "viewer")]);
        await service.ReloadAsync();

        Assert.IsFalse(await service.HasPermissionAsync(Alice, new PermissionId("ano.stats.view")));
    }

    [TestMethod]
    public async Task ModuleDataAuthorizationStore_RoundTripsState()
    {
        var moduleStore = new MemoryModuleDataStore();
        var store = new ModuleDataAuthorizationStore(moduleStore);
        var state = new AuthorizationState(
            [Role("admin", immunity: 25, tags: ["vip"], rules: [Allow("ano.admin.*")])],
            [Assignment(Alice, "admin")]);

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();

        Assert.IsNotNull(loaded);
        Assert.AreEqual(state, loaded);
        Assert.AreEqual(new ModuleId("authorization"), moduleStore.LastModule);
    }

    private static AuthorizationService CreateService(AuthorizationState state)
        => new(new InMemoryAuthorizationStore(state), state);

    private static AuthorizationRole Role(
        string id,
        int immunity = 0,
        IReadOnlyCollection<RoleId>? parents = null,
        IReadOnlyCollection<PermissionRule>? rules = null,
        IReadOnlyCollection<string>? tags = null)
        => new(new RoleId(id), immunity, parents ?? [], rules ?? [], tags ?? []);

    private static PlayerAuthorization Assignment(PlayerId player, params string[] roles)
        => new(player, roles.Select(role => new RoleId(role)).ToArray(), []);

    private static PermissionRule Allow(string pattern) => new(pattern, PermissionEffect.Allow);

    private static PermissionRule Deny(string pattern) => new(pattern, PermissionEffect.Deny);

    private sealed class InMemoryAuthorizationStore : IAuthorizationStore
    {
        public InMemoryAuthorizationStore(AuthorizationState? state = null) => State = state;

        public AuthorizationState? State { get; set; }

        public ValueTask<AuthorizationState?> LoadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(State);

        public ValueTask SaveAsync(AuthorizationState state, CancellationToken cancellationToken = default)
        {
            State = state;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MemoryModuleDataStore : IModuleDataStore
    {
        private string? _json;

        public ModuleId? LastModule { get; private set; }

        public ValueTask<string?> GetAsync(ModuleId module, string key, CancellationToken cancellationToken = default)
        {
            LastModule = module;
            return ValueTask.FromResult(_json);
        }

        public ValueTask SetAsync(ModuleId module, string key, string json, CancellationToken cancellationToken = default)
        {
            LastModule = module;
            _json = json;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(ModuleId module, string key, CancellationToken cancellationToken = default)
        {
            LastModule = module;
            var existed = _json is not null;
            _json = null;
            return ValueTask.FromResult(existed);
        }
    }
}
