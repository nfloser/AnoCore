using AnoCore.Modules.AnoVeto;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
public sealed class AnoVetoMapDraftTests
{
    private static AnoVetoMapDraft NewDraft()
        => new(Enumerable.Range(1, 12).Select(i => ($"workshop-{i}", $"Map {i:00}")));

    [TestMethod]
    public void Picker_RequiresEightDistinctEligibleMaps()
    {
        var draft = NewDraft();
        Assert.IsFalse(draft.CanConfirm);
        Assert.ThrowsExactly<InvalidOperationException>(() => draft.Confirm());
        for (var i = 1; i <= 8; i++)
            Assert.IsTrue(draft.Toggle($"workshop-{i}"));

        Assert.IsTrue(draft.CanConfirm);
        Assert.HasCount(8, draft.Confirm());
        Assert.IsFalse(draft.Toggle("workshop-9"));
        Assert.IsFalse(draft.Toggle("missing"));
        Assert.HasCount(8, draft.SelectedMapIds);
    }

    [TestMethod]
    public void Picker_DeselectAndReselectMaintainsInsertionOrder()
    {
        var draft = NewDraft();
        for (var i = 1; i <= 8; i++) draft.Toggle($"workshop-{i}");

        Assert.IsTrue(draft.Toggle("WORKSHOP-3"));
        Assert.IsFalse(draft.CanConfirm);
        Assert.IsTrue(draft.Toggle("workshop-9"));
        Assert.AreEqual("workshop-9", draft.Confirm()[7]);
        Assert.IsFalse(draft.Confirm().Contains("workshop-3"));
    }

    [TestMethod]
    public void Browse_IsDeterministicBoundedAndFiltersByNameOrId()
    {
        var draft = NewDraft();
        var results = draft.Browse("Map 0", 2, 3);
        Assert.HasCount(3, results);
        Assert.AreEqual("Map 03", results[0].DisplayName);
        Assert.HasCount(1, draft.Browse("WORKSHOP-12", 0, 10));
        Assert.HasCount(0, draft.Browse("unlisted", 0, 10));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => draft.Browse(null, -1, 8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => draft.Browse(null, 0, 0));
    }

    [TestMethod]
    public void Draft_RejectsDuplicateCatalogIdsAndIncompleteCatalog()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new AnoVetoMapDraft(
            Enumerable.Range(1, 7).Select(i => ($"id-{i}", $"Map {i}"))));
        Assert.ThrowsExactly<ArgumentException>(() => new AnoVetoMapDraft(
            Enumerable.Repeat(("same", "Same Map"), 8)));
    }

    [TestMethod]
    public void Confirm_ReturnsSnapshotNotMutableDraftStorage()
    {
        var draft = NewDraft();
        for (var i = 1; i <= 8; i++) draft.Toggle($"workshop-{i}");
        var snapshot = draft.Confirm();
        draft.Toggle("workshop-1");
        Assert.HasCount(8, snapshot);
        Assert.HasCount(7, draft.SelectedMapIds);
    }
}
