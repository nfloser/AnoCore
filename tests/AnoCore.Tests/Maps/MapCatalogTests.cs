using AnoCore.Abstractions.Maps;
using AnoCore.Runtime.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Maps;

[TestClass]
public sealed class MapCatalogTests
{
    [TestMethod]
    public void Constructor_ProvidesDeterministicNameAndIdLookup()
    {
        var catalog = new MapCatalog(
        [
            new MapDefinition("Mirage", "de_mirage", 3070244462),
            new MapDefinition("Inferno", "de_inferno"),
        ]);

        Assert.IsTrue(catalog.TryGetByName(" mirage ", out var mirage));
        Assert.AreEqual("de_mirage", mirage!.MapId);
        Assert.IsTrue(catalog.TryGetByMapId("DE_INFERNO", out var inferno));
        Assert.AreEqual("Inferno", inferno!.DisplayName);
        CollectionAssert.AreEqual(new[] { "Inferno", "Mirage" }, catalog.All.Select(map => map.DisplayName).ToArray());
    }

    [TestMethod]
    public void Constructor_RejectsDuplicateDisplayNamesCaseInsensitively()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new MapCatalog(
        [
            new MapDefinition("Mirage", "de_mirage"),
            new MapDefinition("MIRAGE", "workshop_mirage"),
        ]));
    }

    [TestMethod]
    public void Constructor_RejectsDuplicateMapIdsAndWorkshopIds()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new MapCatalog(
        [
            new MapDefinition("One", "de_same"),
            new MapDefinition("Two", "DE_SAME"),
        ]));

        Assert.ThrowsExactly<ArgumentException>(() => new MapCatalog(
        [
            new MapDefinition("One", "de_one", 42),
            new MapDefinition("Two", "de_two", 42),
        ]));
    }

    [TestMethod]
    public void Definition_RejectsUnsafeOrMissingIdentifiers()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new MapDefinition("", "de_test"));
        Assert.ThrowsExactly<ArgumentException>(() => new MapDefinition("Test", ""));
        Assert.ThrowsExactly<ArgumentException>(() => new MapDefinition("Test", "de_test;quit"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MapDefinition("Test", "de_test", 0));
    }
}
