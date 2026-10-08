using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Configuration;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class RoleChatTagTests
{
    private static readonly PlayerId Player = new(76561198000015001);

    [TestMethod]
    public void GroupsUseExactMembershipAndStablePriorityRegardlessOfInputOrder()
    {
        var configuration = Enabled();
        configuration.Groups.Reverse();
        var policy = RoleChatTagPolicy.Compile(configuration);
        Assert.AreEqual("\x0F[ADMIN]\x01 ", policy.Resolve(Player, ["#css/host", "#css/admin"], PlayerTeam.Terrorist));
        Assert.AreEqual("\x09[DEV]\x01 ", policy.Resolve(Player, ["#css/og", "#css/dev"], PlayerTeam.Terrorist));
        Assert.AreEqual("\x04[ANOMEME]\x01 ", policy.Resolve(Player, ["#css/unknown", "#CSS/ADMIN"], PlayerTeam.Unknown));
    }

    [TestMethod]
    public void PersonalFounderOverridesGroupsWithoutModifyingMembershipAndSnapshotIsImmutable()
    {
        var configuration = Enabled();
        configuration.PlayerOverrides.Add(Player.SteamId64, new("[FOUNDER]", "Red"));
        var groups = new HashSet<string> { "#css/admin" };
        var policy = RoleChatTagPolicy.Compile(configuration);
        configuration.Groups.Clear();
        configuration.PlayerOverrides.Clear();
        configuration.DefaultTag = new("[CHANGED]", "None");
        Assert.AreEqual("\x07[FOUNDER]\x01 ", policy.Resolve(Player, groups, PlayerTeam.Unknown));
        CollectionAssert.AreEqual(new[] { "#css/admin" }, groups.ToArray());
        Assert.AreEqual("\x0F[ADMIN]\x01 ", policy.Resolve(new PlayerId(76561198000015002), groups, PlayerTeam.Unknown));
    }

    [TestMethod]
    public void DisabledAndAbsentFallbackDoNotAddPrefixesAndTeamColorResets()
    {
        Assert.IsNull(RoleChatTagPolicy.Compile(RoleChatTagConfiguration.Default)
            .Resolve(Player, ["#css/admin"], PlayerTeam.Terrorist));
        var configuration = Enabled();
        configuration.DefaultTag = null;
        Assert.IsNull(RoleChatTagPolicy.Compile(configuration).Resolve(Player, [], PlayerTeam.Unknown));
        configuration.DefaultTag = new("[TEAM]", "Team");
        Assert.AreEqual("\x0B[TEAM]\x01 ", RoleChatTagPolicy.Compile(configuration)
            .Resolve(Player, [], PlayerTeam.CounterTerrorist));
    }

    [TestMethod]
    public void RejectsAmbiguousOrUnsafeConfiguration()
    {
        var configuration = Enabled();
        configuration.Groups.Add(new("#css/admin", 1, new("[DUPLICATE]", "None")));
        Assert.IsNotEmpty(RoleChatTagConfiguration.Validate(configuration));
        configuration = Enabled();
        configuration.Groups.Add(new("#css/another", configuration.Groups[0].Priority, new("[TIE]", "None")));
        Assert.IsNotEmpty(RoleChatTagConfiguration.Validate(configuration));
        configuration = Enabled();
        configuration.DefaultTag = new("{message}\x04", "Magenta");
        Assert.IsNotEmpty(RoleChatTagConfiguration.Validate(configuration));
        configuration = Enabled();
        configuration.PlayerOverrides.Add(0, new("[INVALID]", "None"));
        Assert.IsNotEmpty(RoleChatTagConfiguration.Validate(configuration));
        Assert.Throws<ArgumentException>(() => RoleChatTagPolicy.Compile(configuration));
    }

    [TestMethod]
    public async Task ReloadReplacesWholePolicyInvalidReloadRetainsPreviousAndDisposeUnregisters()
    {
        var root = Path.Combine(Path.GetTempPath(), "anocore-role-tags", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonConfigStore(root);
            var reloads = new ConfigReloadRegistry();
            await store.SaveAsync("role-chat-tags", Enabled());
            using var module = await RoleChatTagModule.CreateAsync(store, reloads);
            Assert.AreEqual("\x0F[ADMIN]\x01 ", module.Resolve(Player, ["#css/admin"], PlayerTeam.Unknown));
            var next = Enabled();
            next.Groups.Clear();
            next.DefaultTag = new("[NEW]", "None");
            await store.SaveAsync("role-chat-tags", next);
            await reloads.ReloadAsync("role-chat-tags");
            Assert.AreEqual("[NEW] ", module.Resolve(Player, ["#css/admin"], PlayerTeam.Unknown));
            next.DefaultTag = new("{unsafe}", "None");
            await store.SaveAsync("role-chat-tags", next);
            await Assert.ThrowsAsync<Exception>(async () => await reloads.ReloadAsync("role-chat-tags"));
            Assert.AreEqual("[NEW] ", module.Resolve(Player, [], PlayerTeam.Unknown));
            module.Dispose();
            Assert.IsEmpty(reloads.Configurations);
            Assert.IsNull(module.Resolve(Player, [], PlayerTeam.Unknown));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static RoleChatTagConfiguration Enabled() => new()
    {
        Enabled = true,
        DefaultTag = new("[ANOMEME]", "Green"),
        Groups =
        [
            new("#css/admin", 400, new("[ADMIN]", "LightRed")),
            new("#css/dev", 300, new("[DEV]", "Yellow")),
            new("#css/host", 200, new("[HOST]", "Purple")),
            new("#css/og", 100, new("[OG]", "DarkBlue")),
        ],
    };
}
