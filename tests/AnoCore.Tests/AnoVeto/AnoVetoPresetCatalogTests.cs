using AnoCore.Modules.AnoVeto;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
public sealed class AnoVetoPresetCatalogTests
{
    private static AnoVetoPreset Make(ulong owner, string name)
        => new(owner, name, Enumerable.Range(1, 8).Select(i => $"de_map{i}"));

    [TestMethod]
    public void CreateListAndGet_IsolateOwners()
    {
        var store = new AnoVetoPresetCatalog();
        Assert.AreEqual(1L, store.Create(Make(10, "Classic")));
        store.Create(Make(20, "Classic"));
        Assert.HasCount(1, store.List(10));
        Assert.HasCount(1, store.List(20));
        Assert.IsTrue(store.TryGet(10, " classic ", out var preset, out var revision));
        Assert.AreEqual((ulong)10, preset!.OwnerSteamId64);
        Assert.AreEqual(1L, revision);
        Assert.IsFalse(store.TryGet(30, "Classic", out _, out _));
        Assert.ThrowsExactly<InvalidOperationException>(() => store.Create(Make(10, "CLASSIC")));
    }

    [TestMethod]
    public void ReplaceRequiresCorrectRevisionAndRejectsOwnerChange()
    {
        var store = new AnoVetoPresetCatalog();
        store.Create(Make(10, "Classic"));
        var version = store.Replace(10, "Classic", 1, Make(10, "New"));
        Assert.AreEqual(2L, version);
        Assert.IsFalse(store.TryGet(10, "Classic", out _, out _));
        Assert.IsTrue(store.TryGet(10, "New", out _, out var current));
        Assert.AreEqual(2L, current);
        Assert.ThrowsExactly<InvalidOperationException>(() => store.Replace(10, "New", 1, Make(10, "Stale")));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => store.Replace(10, "New", 2, Make(20, "Other")));
    }

    [TestMethod]
    public void DeleteRequiresCorrectOwnerAndVersion()
    {
        var store = new AnoVetoPresetCatalog();
        store.Create(Make(10, "Classic"));
        Assert.ThrowsExactly<InvalidOperationException>(() => store.Delete(20, "Classic", 1));
        Assert.ThrowsExactly<InvalidOperationException>(() => store.Delete(10, "Classic", 2));
        store.Delete(10, "Classic", 1);
        Assert.HasCount(0, store.List(10));
    }

    [TestMethod]
    public void EnforceBoundedPresetCount()
    {
        var store = new AnoVetoPresetCatalog();
        for (var i = 0; i < AnoVetoPresetCatalog.MaxPresetsPerOwner; i++)
            store.Create(Make(10, $"Preset{i}"));
        Assert.ThrowsExactly<InvalidOperationException>(() => store.Create(Make(10, "Overflow")));
        Assert.AreEqual(1L, store.Create(Make(20, "Allowed")));
    }
}
