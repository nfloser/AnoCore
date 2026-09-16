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
