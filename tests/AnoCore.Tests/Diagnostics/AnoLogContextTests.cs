using AnoCore.Abstractions.Diagnostics;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Diagnostics;

[TestClass]
public sealed class AnoLogContextTests
{
    [TestMethod]
    public void ToProperties_IncludesOnlyKnownStructuredFields()
    {
        var context = new AnoLogContext(
            new ModuleId("veto"),
            new PlayerId(76561198000000001),
            "create_vote");

        var properties = context.ToProperties();

        Assert.AreEqual("ano.veto", properties["AnoModule"]);
        Assert.AreEqual(76561198000000001UL, properties["SteamId"]);
        Assert.AreEqual("create_vote", properties["AnoOperation"]);
    }

    [TestMethod]
    public void ToProperties_OmitsNullFields()
    {
        var properties = new AnoLogContext(null, null, null).ToProperties();

        Assert.HasCount(0, properties);
    }
}
