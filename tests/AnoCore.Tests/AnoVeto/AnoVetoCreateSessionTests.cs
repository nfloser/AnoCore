using AnoCore.Modules.AnoVeto;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
public sealed class AnoVetoCreateSessionTests
{
    private static AnoVetoCreateSession Session()
        => new(Enumerable.Range(1, 12).Select(i => ($"map-{i}", $"Map {i:00}")));

    [TestMethod]
    public void Home_DoesNotPublishOrSelectMaps()
    {
        var session = Session();
        Assert.AreEqual(AnoVetoCreateSession.Screen.Home, session.CurrentScreen);
        Assert.IsFalse(session.SelectMap("map-1"));
        Assert.HasCount(0, session.Draft.SelectedMapIds);
        Assert.HasCount(0, session.Browse(0, 8));
    }

    [TestMethod]
    public void Create_SupportsFilterAndPreservesDraftAcrossNavigation()
    {
        var session = Session();
        session.OpenCreate();
        session.ApplyFilter("Map 01");
        Assert.HasCount(1, session.Browse(0, 8));
        Assert.IsTrue(session.SelectMap("map-1"));
        session.OpenSaved();
        Assert.IsFalse(session.SelectMap("map-2"));
        session.Back();
        Assert.AreEqual(AnoVetoCreateSession.Screen.Home, session.CurrentScreen);
        Assert.AreEqual(string.Empty, session.Filter);
        session.OpenCreate();
        Assert.HasCount(1, session.Draft.SelectedMapIds);
    }

    [TestMethod]
    public void InvalidLongSearchRejected()
    {
        var session = Session();
        session.OpenCreate();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => session.ApplyFilter(new string('a', 81)));
        session.Back();
        Assert.ThrowsExactly<InvalidOperationException>(() => session.ApplyFilter("map"));
    }
}
