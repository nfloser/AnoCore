using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Permissions;

namespace AnoCore.Tests.Permissions;

[TestClass]
public sealed class RoleAdministrationTests
{
    private static readonly PlayerId Admin = new(76561198000000101);
    private static readonly PlayerId Player = new(76561198000000102);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-08T00:00:00Z");

    [TestMethod]
    public async Task ExpiryRemovesInheritedPermissionsTagsAndImmunityWithoutReload()
    {
        var clock = new Clock { Now = Now };
        var state = new AuthorizationState([
            new(new("base"), 10, [], [new("ano.stats.*", PermissionEffect.Allow)], ["vip"]),
            new(new("vip"), 20, [new("base")], [], [])],
            [new(Player, [], [], [new(new("vip"), Now.AddMinutes(1))])]);
        var store = new Store(state);
        var service = new AuthorizationService(store, state, clock);
        Assert.IsTrue(await service.HasPermissionAsync(Player, new("ano.stats.view")));
        Assert.IsTrue(await service.HasTagAsync(Player, "vip"));
        Assert.AreEqual(20, await service.GetImmunityAsync(Player));
        clock.Now = Now.AddMinutes(1);
        Assert.IsFalse(await service.HasPermissionAsync(Player, new("ano.stats.view")));
        Assert.IsFalse(await service.HasTagAsync(Player, "vip"));
        Assert.AreEqual(0, await service.GetImmunityAsync(Player));
    }

    [TestMethod]
    public async Task GrantsRevokeAndDelegationCeilingsPreserveIndependentPolicy()
    {
        var state = Policy();
        var granted = await RoleAdministrationPolicy.ChangeAsync(state, Admin, Player, new("vip"), Now.AddDays(1), false, Now);
        Assert.HasCount(1, granted.Players.Single(p => p.PlayerId == Player).TimedRoles);
        var revoked = await RoleAdministrationPolicy.ChangeAsync(granted, Admin, Player, new("vip"), null, true, Now);
        Assert.IsEmpty(revoked.Players.Single(p => p.PlayerId == Player).TimedRoles);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await RoleAdministrationPolicy.ChangeAsync(state, Admin, Admin, new("vip"), null, false, Now));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await RoleAdministrationPolicy.ChangeAsync(state, Admin, Player, new("owner"), null, false, Now));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await RoleAdministrationPolicy.ChangeAsync(state, Player, Admin, new("vip"), null, false, Now));
    }

    [TestMethod]
    public async Task ConsoleBootstrapAndJsonRoundtripPreservePermanentAndTimedGrants()
    {
        var state = await RoleAdministrationPolicy.ChangeAsync(Policy(), null, Player, new("vip"), null, false, Now);
        state = await RoleAdministrationPolicy.ChangeAsync(state, null, Player, new("helper"), Now.AddDays(1), false, Now);
        var json = System.Text.Json.JsonSerializer.Serialize(state);
        Assert.AreEqual(state, System.Text.Json.JsonSerializer.Deserialize<AuthorizationState>(json));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await RoleAdministrationPolicy.ChangeAsync(state, null, Player, new("vip"), Now, false, Now));
        Assert.ThrowsExactly<ArgumentException>(() => new AuthorizationState(Policy().Roles,
            [new(Player, [], [], [new(new("missing"), Now.AddDays(1))])]));
    }

    public static AuthorizationState Policy() => new([
        new(new("vip"), 1, [], [new("ano.stats.*", PermissionEffect.Allow)], ["vip"]),
        new(new("helper"), 0, [], [], []),
        new(new("admin"), 50, [new("vip")], [new("ano.admin.roles.*", PermissionEffect.Allow)], []),
        new(new("owner"), 100, [], [new("ano.*", PermissionEffect.Allow)], [])],
        [new(Admin, [new("admin")], []), new(Player, [], [])]);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Store(AuthorizationState state) : IAuthorizationStore
    {
        public ValueTask<AuthorizationState?> LoadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<AuthorizationState?>(state);
        public ValueTask SaveAsync(AuthorizationState value, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
