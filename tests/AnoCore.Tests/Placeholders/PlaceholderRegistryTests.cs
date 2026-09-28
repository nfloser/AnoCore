using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Placeholders;
using AnoCore.Runtime.Placeholders;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Placeholders;

[TestClass]
public sealed class PlaceholderRegistryTests
{
    [TestMethod]
    public async Task ResolveAsync_ReplacesRegisteredPlaceholdersAndLeavesUnknownTokens()
    {
        var registry = new PlaceholderRegistry();
        var owner = new ModuleId("stats");
        using var registration = registry.Register(
            owner,
            "player.rank",
            (_, _) => ValueTask.FromResult<string?>("Gold"));

        var result = await registry.ResolveAsync(
            "Rank: {player.rank} / {unknown}",
            PlaceholderContext.Empty);

        Assert.AreEqual("Rank: Gold / {unknown}", result);
    }

    [TestMethod]
    public void Register_RejectsDuplicatePlaceholderEvenAcrossOwners()
    {
        var registry = new PlaceholderRegistry();
        using var first = registry.Register(
            new ModuleId("stats"),
            "player.rank",
            (_, _) => ValueTask.FromResult<string?>("Gold"));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            registry.Register(
                new ModuleId("admin"),
                "player.rank",
                (_, _) => ValueTask.FromResult<string?>("Admin")));
    }

    [TestMethod]
    public async Task Dispose_RemovesOnlyThatRegistration()
    {
        var registry = new PlaceholderRegistry();
        var first = registry.Register(
            new ModuleId("stats"),
            "first",
            (_, _) => ValueTask.FromResult<string?>("1"));
        using var second = registry.Register(
            new ModuleId("stats"),
            "second",
            (_, _) => ValueTask.FromResult<string?>("2"));
        first.Dispose();
        first.Dispose();

        var result = await registry.ResolveAsync("{first}-{second}", PlaceholderContext.Empty);

        Assert.AreEqual("{first}-2", result);
    }

    [TestMethod]
    public async Task RemoveOwner_RemovesAllOwnedRegistrations()
    {
        var registry = new PlaceholderRegistry();
        var owner = new ModuleId("stats");
        registry.Register(owner, "one", (_, _) => ValueTask.FromResult<string?>("1"));
        registry.Register(owner, "two", (_, _) => ValueTask.FromResult<string?>("2"));
        registry.Register(new ModuleId("admin"), "admin", (_, _) => ValueTask.FromResult<string?>("A"));

        var removed = registry.RemoveOwner(owner);
        var result = await registry.ResolveAsync("{one}-{two}-{admin}", PlaceholderContext.Empty);

        Assert.AreEqual(2, removed);
        Assert.AreEqual("{one}-{two}-A", result);
    }


    [TestMethod]
    public async Task Prioritized_HighestApplicableWinsAndNullFallsBack()
    {
        var registry = new PlaceholderRegistry();
        using var rank = registry.RegisterPrioritized(
            new ModuleId("ranks"), "chat.tag", 0,
            (_, _) => ValueTask.FromResult<string?>("[Rank]"));
        using var absentVip = registry.RegisterPrioritized(
            new ModuleId("vip"), "chat.tag", 100,
            (_, _) => ValueTask.FromResult<string?>(null));
        using var staff = registry.RegisterPrioritized(
            new ModuleId("staff"), "chat.tag", 200,
            (_, _) => ValueTask.FromResult<string?>("[Staff]"));

        Assert.AreEqual("[Staff]", await registry.ResolveAsync(
            "{chat.tag}", PlaceholderContext.Empty));

        staff.Dispose();

        Assert.AreEqual("[Rank]", await registry.ResolveAsync(
            "{chat.tag}", PlaceholderContext.Empty));
    }

    [TestMethod]
    public async Task Prioritized_EmptyValueSuppressesLowerPriority()
    {
        var registry = new PlaceholderRegistry();
        using var rank = registry.RegisterPrioritized(
            new ModuleId("ranks"), "chat.tag", 0,
            (_, _) => ValueTask.FromResult<string?>("[Rank]"));
        using var hidden = registry.RegisterPrioritized(
            new ModuleId("privacy"), "chat.tag", 100,
            (_, _) => ValueTask.FromResult<string?>(string.Empty));

        Assert.AreEqual("tag:", await registry.ResolveAsync(
            "tag:{chat.tag}", PlaceholderContext.Empty));
    }

    [TestMethod]
    public void Prioritized_RejectsAmbiguousOrMixedOwnership()
    {
        var registry = new PlaceholderRegistry();
        using var first = registry.RegisterPrioritized(
            new ModuleId("ranks"), "chat.tag", 0,
            (_, _) => ValueTask.FromResult<string?>("[Rank]"));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            registry.RegisterPrioritized(
                new ModuleId("other"), "chat.tag", 0,
                (_, _) => ValueTask.FromResult<string?>("[Other]")));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            registry.Register(
                new ModuleId("exclusive"), "chat.tag",
                (_, _) => ValueTask.FromResult<string?>("[Exclusive]")));
    }

    [TestMethod]
    public async Task RemoveOwner_RemovesOnlyItsPrioritizedProviders()
    {
        var registry = new PlaceholderRegistry();
        var higherOwner = new ModuleId("staff");
        registry.RegisterPrioritized(
            higherOwner, "chat.tag", 100,
            (_, _) => ValueTask.FromResult<string?>("[Staff]"));
        using var rank = registry.RegisterPrioritized(
            new ModuleId("ranks"), "chat.tag", 0,
            (_, _) => ValueTask.FromResult<string?>("[Rank]"));

        Assert.AreEqual(1, registry.RemoveOwner(higherOwner));
        Assert.AreEqual("[Rank]", await registry.ResolveAsync(
            "{chat.tag}", PlaceholderContext.Empty));
    }

    [TestMethod]
    public void Register_NormalizesPlaceholderNames()
    {
        var registry = new PlaceholderRegistry();
        using var registration = registry.Register(
            new ModuleId("stats"),
            " Player.Rank ",
            (_, _) => ValueTask.FromResult<string?>("Gold"));

        Assert.IsTrue(registry.Contains("player.rank"));
    }
}
