using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Settings;
using AnoCore.Runtime.Settings;

namespace AnoCore.Tests.Settings;

[TestClass]
public sealed class PlayerToggleCatalogTests
{
    private static readonly ModuleId Owner = new("ano.example");
    private static readonly ModuleId Other = new("ano.other");

    [TestMethod]
    public void Descriptor_RejectsInvalidLabelsDescriptionsAndKeys()
    {
        var key = new PlayerSettingKey<bool>("chat.compact", false);
        Assert.ThrowsExactly<ArgumentException>(
            () => new PlayerToggleSetting(key, " ", "Description"));
        Assert.ThrowsExactly<ArgumentException>(
            () => new PlayerToggleSetting(key, "Label\nInjected", "Description"));
        Assert.ThrowsExactly<ArgumentException>(
            () => new PlayerToggleSetting(key, new string('a', 65), "Description"));
        Assert.ThrowsExactly<ArgumentException>(
            () => new PlayerToggleSetting(key, "Label", new string('a', 257)));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new PlayerToggleSetting(null!, "Label", "Description"));
    }

    [TestMethod]
    public void Register_ListsSortedSnapshotsAndRejectsDuplicateAcrossModules()
    {
        var catalog = new PlayerToggleCatalog();
        var z = new PlayerToggleSetting(
            new PlayerSettingKey<bool>("ui.z", false), "Z", "");
        var a = new PlayerToggleSetting(
            new PlayerSettingKey<bool>("ui.a", true), "A", "");
        using var first = catalog.Register(Owner, z);
        using var second = catalog.Register(Other, a);
        var snapshot = catalog.GetAll();

        CollectionAssert.AreEqual(new[] { "ui.a", "ui.z" },
            snapshot.Select(setting => setting.Key.Name).ToArray());
        Assert.IsTrue(catalog.TryGet("ui.z", out var found));
        Assert.AreSame(z, found);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => catalog.Register(Other, z));
        first.Dispose();
        Assert.AreEqual(2, snapshot.Count);
        Assert.IsFalse(catalog.TryGet("ui.z", out _));
    }

    [TestMethod]
    public void UnregisterAll_OnlyRemovesOwnerAndStaleHandleCannotRemoveReplacement()
    {
        var catalog = new PlayerToggleCatalog();
        var option = new PlayerToggleSetting(
            new PlayerSettingKey<bool>("ui.opt", false), "Option", "");
        using var old = catalog.Register(Owner, option);
        using var other = catalog.Register(Other, new PlayerToggleSetting(
            new PlayerSettingKey<bool>("ui.other", false), "Other", ""));
        catalog.UnregisterAll(Owner);
        using var replacement = catalog.Register(Other, option);
        old.Dispose();

        Assert.IsTrue(catalog.TryGet("ui.opt", out var current));
        Assert.AreSame(option, current);
        catalog.UnregisterAll(Other);
        Assert.AreEqual(0, catalog.GetAll().Count);
    }

    [TestMethod]
    public void Catalog_BoundsRegistrationsAndSupportsConcurrentSnapshots()
    {
        var catalog = new PlayerToggleCatalog();
        var handles = new List<IDisposable>();
        for (var index = 0; index < 64; index++)
        {
            handles.Add(catalog.Register(Owner, new PlayerToggleSetting(
                new PlayerSettingKey<bool>($"test.{index}", false),
                $"Setting {index}", "")));
        }

        Assert.ThrowsExactly<InvalidOperationException>(
            () => catalog.Register(Owner, new PlayerToggleSetting(
                new PlayerSettingKey<bool>("test.overflow", false), "Overflow", "")));
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        Parallel.For(0, 32, iteration =>
        {
            try
            {
                Assert.AreEqual(64, catalog.GetAll().Count);
                Assert.IsTrue(catalog.TryGet($"test.{iteration}", out var found));
                Assert.IsNotNull(found);
            }
            catch (Exception exception)
            {
                errors.Enqueue(exception);
            }
        });
        Assert.AreEqual(0, errors.Count);
        foreach (var handle in handles)
            handle.Dispose();
        Assert.AreEqual(0, catalog.GetAll().Count);
    }
}
